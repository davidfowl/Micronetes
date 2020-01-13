using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Micronetes.Hosting.Infrastructure;
using Micronetes.Hosting.Logging;
using Micronetes.Hosting.Metrics;
using Micronetes.Hosting.Model;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventSource;
using Microsoft.Hosting.Logging;

namespace Micronetes.Hosting
{
    public class OutOfProcessExecutionTarget : IExecutionTarget
    {
        private static readonly string MicrosoftExtensionsLoggingProviderName = "Microsoft-Extensions-Logging";
        private static readonly string SystemRuntimeEventSourceName = "System.Runtime";
        private static readonly string MicrosoftAspNetCoreHostingEventSourceName = "Microsoft.AspNetCore.Hosting";

        private readonly ILogger _logger;
        private readonly bool _debugMode;

        public OutOfProcessExecutionTarget(ILogger logger, bool debugMode = true)
        {
            _logger = logger;
            _debugMode = debugMode;
        }

        public Task StartAsync(Application application)
        {
            var tasks = new Task[application.Services.Count];
            var index = 0;
            foreach (var s in application.Services)
            {
                tasks[index++] = s.Value.Description.External ? Task.CompletedTask : LaunchService(application, s.Value);
            }

            return Task.WhenAll(tasks);
        }

        public Task StopAsync(Application application)
        {
            return KillRunningProcesses(application.Services);
        }

        private Task LaunchService(Application application, Service service)
        {
            var serviceDescription = service.Description;

            if (serviceDescription.DockerImage != null)
            {
                return Docker.RunAsync(_logger, service);
            }

            var serviceName = serviceDescription.Name;

            var path = "";
            var workingDirectory = "";
            var args = service.Description.Args ?? "";

            if (serviceDescription.Project != null)
            {
                var fullProjectPath = Path.GetFullPath(Path.Combine(application.ContextDirectory, serviceDescription.Project));
                path = GetExePath(fullProjectPath);
                workingDirectory = Path.GetDirectoryName(fullProjectPath);

                service.Status["projectFilePath"] = fullProjectPath;
            }
            else
            {
                path = Path.GetFullPath(Path.Combine(application.ContextDirectory, serviceDescription.Executable));
                workingDirectory = serviceDescription.WorkingDirectory != null ?
                    Path.GetFullPath(Path.Combine(application.ContextDirectory, serviceDescription.WorkingDirectory)) :
                    Path.GetDirectoryName(path);

                // If this is a dll then use dotnet to run it
                if (Path.GetExtension(path) == ".dll")
                {
                    args = $"\"{path}\" {args}".Trim();
                    path = "dotnet";
                }
            }

            service.Status["executablePath"] = path;
            service.Status["workingDirectory"] = workingDirectory;
            service.Status["args"] = args;
            service.Status["debugMode"] = _debugMode;

            var processInfo = new ProcessInfo
            {
                Threads = new Thread[service.Description.Replicas.Value]
            };

            void RunApplication(IEnumerable<(int Port, string Protocol)> ports)
            {
                var hasPorts = ports.Any();
                var restarts = 0;

                var environment = new Dictionary<string, string>
                {
                    // Default to development environment
                    ["DOTNET_ENVIRONMENT"] = "Development"
                };

                application.PopulateEnvironment(service, (k, v) => environment[k] = v);

                if (_debugMode)
                {
                    environment["DOTNET_STARTUP_HOOKS"] = typeof(Hosting.Runtime.HostingRuntimeHelpers).Assembly.Location;
                }

                if (hasPorts)
                {
                    // These ports should also be passed in not assuming ASP.NET Core
                    environment["ASPNETCORE_URLS"] = string.Join(";", ports.Select(p => $"{p.Protocol ?? "http"}://localhost:{p.Port}"));
                }

                while (!processInfo.StoppedTokenSource.IsCancellationRequested)
                {
                    var replica = serviceName + "_" + Guid.NewGuid().ToString().Substring(0, 10).ToLower();
                    var status = service.Replicas[replica] = new ServiceReplica();
                    // This isn't your host name
                    environment["APP_INSTANCE"] = replica;

                    status["exitCode"] = null;
                    status["pid"] = null;
                    status["env"] = environment;

                    if (hasPorts)
                    {
                        status["ports"] = ports.Select(p => p.Port);
                    }

                    service.Status["restarts"] = restarts;

                    _logger.LogInformation("Launching service {ServiceName} from {ExePath} {args}", replica, path, args);

                    var metricsTokenSource = CancellationTokenSource.CreateLinkedTokenSource(processInfo.StoppedTokenSource.Token);

                    var eventPipeThread = new Thread(state => ProcessEvents(application.LoggerFactory, service.Description.Name, (int)state, replica, status, metricsTokenSource.Token));

                    try
                    {
                        var result = ProcessUtil.Run(path, args,
                            environmentVariables: environment,
                            workingDirectory: workingDirectory,
                            outputDataReceived: data =>
                            {
                                if (data == null)
                                {
                                    return;
                                }

                                service.Logs.Add("[" + replica + "]: " + data);
                            },
                            onStart: pid =>
                            {
                                if (hasPorts)
                                {
                                    _logger.LogInformation("{ServiceName} running on process id {PID} bound to {Address}", replica, pid, string.Join(", ", ports.Select(p => $"{p.Protocol ?? "http"}://localhost:{p.Port}")));
                                }
                                else
                                {
                                    _logger.LogInformation("{ServiceName} running on process id {PID}", replica, pid);
                                }

                                status["pid"] = pid;

                                eventPipeThread.Start(pid);
                            },
                            throwOnError: false,
                            cancellationToken: processInfo.StoppedTokenSource.Token);

                        status["exitCode"] = result.ExitCode;

                        if (status["pid"] != null)
                        {
                            metricsTokenSource.Cancel();

                            eventPipeThread.Join();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(0, ex, "Failed to launch process for service {ServiceName}", replica);

                        Thread.Sleep(5000);
                    }

                    restarts++;
                    if (status["exitCode"] != null)
                    {
                        _logger.LogInformation("{ServiceName} process exited with exit code {ExitCode}", replica, status["exitCode"]);
                    }

                    // Remove the replica from the set
                    service.Replicas.TryRemove(replica, out _);
                }
            }

            if (serviceDescription.Bindings.Count > 0)
            {
                // Each replica is assigned a list of internal ports, one mapped to each external
                // port
                for (int i = 0; i < serviceDescription.Replicas; i++)
                {
                    var ports = new List<(int, string)>();
                    foreach (var binding in serviceDescription.Bindings)
                    {
                        if (binding.Port == null)
                        {
                            continue;
                        }

                        ports.Add((service.PortMap[binding.Port.Value][i], binding.Protocol));
                    }

                    processInfo.Threads[i] = new Thread(() => RunApplication(ports));
                }
            }
            else
            {
                for (int i = 0; i < service.Description.Replicas; i++)
                {
                    processInfo.Threads[i] = new Thread(() => RunApplication(Enumerable.Empty<(int, string)>()));
                }
            }

            for (int i = 0; i < service.Description.Replicas; i++)
            {
                processInfo.Threads[i].Start();
            }

            service.Items[typeof(ProcessInfo)] = processInfo;

            return Task.CompletedTask;
        }

        private Task KillRunningProcesses(IDictionary<string, Service> services)
        {
            static void KillProcess(Service service)
            {
                if (service.Items.TryGetValue(typeof(ProcessInfo), out var stateObj) && stateObj is ProcessInfo state)
                {
                    // Cancel the token before stopping the process
                    state.StoppedTokenSource.Cancel();
                    foreach (var t in state.Threads)
                    {
                        t.Join();
                    }
                }
                else if (service.Description.DockerImage != null)
                {
                    Docker.Stop(service);
                }
            }

            var index = 0;
            var tasks = new Task[services.Count];
            foreach (var s in services.Values)
            {
                var state = s;
                tasks[index++] = Task.Run(() => KillProcess(state));
            }

            return Task.WhenAll(tasks);
        }

        private static string GetExePath(string projectFilePath)
        {
            // TODO: Use msbuild to get the target path
            var outputFileName = Path.GetFileNameWithoutExtension(projectFilePath) + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
            return Path.Combine(Path.GetDirectoryName(projectFilePath), "bin", "Debug", "netcoreapp3.1", outputFileName);
        }

        private void ProcessEvents(ILoggerFactory loggerFactory, string serviceName, int processId, string replicaName, ServiceReplica replica, CancellationToken cancellationToken)
        {
            var hasEventPipe = false;

            for (int i = 0; i < 10; ++i)
            {
                if (DiagnosticsClient.GetPublishedProcesses().Contains(processId))
                {
                    hasEventPipe = true;
                    break;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                Thread.Sleep(500);
            }

            if (!hasEventPipe)
            {
                _logger.LogInformation("Process id {PID}, does not support event pipe", processId);
                return;
            }

            _logger.LogInformation("Listening for event pipe events for {ServiceName} on process id {PID}", replicaName, processId);

            var scopeState = new Dictionary<string, object>
            {
                { "Application", serviceName },
                { "Instance", replicaName }
            };

            var providers = new List<EventPipeProvider>()
            {
                // Metrics
                new EventPipeProvider(
                    SystemRuntimeEventSourceName,
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.None,
                    new Dictionary<string, string>() {
                        { "EventCounterIntervalSec", "1" }
                    }
                ),
                new EventPipeProvider(
                    MicrosoftAspNetCoreHostingEventSourceName,
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.None,
                    new Dictionary<string, string>() {
                        { "EventCounterIntervalSec", "1" }
                    }
                ),

                // Logging
                new EventPipeProvider(
                    MicrosoftExtensionsLoggingProviderName,
                    EventLevel.LogAlways,
                    (long)(LoggingEventSource.Keywords.JsonMessage | LoggingEventSource.Keywords.FormattedMessage)
                )
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                EventPipeSession session = null;
                var client = new DiagnosticsClient(processId);

                try
                {
                    session = client.StartEventPipeSession(providers);
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug(0, ex, "Failed to start the event pipe session");
                    }

                    // We can't even start the session, wait until the process boots up again to start another metrics thread
                    break;
                }

                void StopSession()
                {
                    try
                    {
                        session.Stop();
                    }
                    catch (EndOfStreamException)
                    {
                        // If the app we're monitoring exits abruptly, this may throw in which case we just swallow the exception and exit gracefully.
                    }
                    // We may time out if the process ended before we sent StopTracing command. We can just exit in that case.
                    catch (TimeoutException)
                    {
                    }
                    // On Unix platforms, we may actually get a PNSE since the pipe is gone with the process, and Runtime Client Library
                    // does not know how to distinguish a situation where there is no pipe to begin with, or where the process has exited
                    // before dotnet-counters and got rid of a pipe that once existed.
                    // Since we are catching this in StopMonitor() we know that the pipe once existed (otherwise the exception would've 
                    // been thrown in StartMonitor directly)
                    catch (PlatformNotSupportedException)
                    {
                    }
                }

                using var _ = cancellationToken.Register(() => StopSession());

                try
                {
                    var source = new EventPipeEventSource(session.EventStream);

                    // Metrics
                    source.Dynamic.All += traceEvent =>
                    {
                        if (traceEvent.EventName.Equals("EventCounters"))
                        {
                            var payloadVal = (IDictionary<string, object>)traceEvent.PayloadValue(0);
                            var eventPayload = (IDictionary<string, object>)payloadVal["Payload"];

                            ICounterPayload payload = CounterPayload.FromPayload(eventPayload);

                            replica.Metrics[payload.Name] = payload.Value;
                        }
                    };

                    // Logging
                    string lastFormattedMessage = "";

                    // TODO: Handle scopes

                    source.Dynamic.AddCallbackForProviderEvent(MicrosoftExtensionsLoggingProviderName, "MessageJson", (traceEvent) =>
                    {
                        // Level, FactoryID, LoggerName, EventID, EventName, ExceptionJson, ArgumentsJson
                        var logLevel = (LogLevel)traceEvent.PayloadByName("Level");
                        var factoryId = (int)traceEvent.PayloadByName("FactoryID");
                        var categoryName = (string)traceEvent.PayloadByName("LoggerName");
                        var eventId = (int)traceEvent.PayloadByName("EventId");
                        var eventName = (string)traceEvent.PayloadByName("EventName");
                        var exceptionJson = (string)traceEvent.PayloadByName("ExceptionJson");
                        var argsJson = (string)traceEvent.PayloadByName("ArgumentsJson");

                        // There's a bug that causes some of the columns to get mixed up
                        if (eventName.StartsWith("{"))
                        {
                            argsJson = exceptionJson;
                            exceptionJson = eventName;
                            eventName = null;
                        }

                        if (string.IsNullOrEmpty(argsJson))
                        {
                            return;
                        }

                        Exception exception = null;
                        
                        var logger = loggerFactory.CreateLogger(categoryName);

                        using var scope = logger.BeginScope(scopeState);

                        try
                        {
                            if (exceptionJson != "{}")
                            {
                                var exceptionMessage = JsonSerializer.Deserialize<JsonElement>(exceptionJson);
                                exception = new LoggerException(exceptionMessage);
                            }

                            var message = JsonSerializer.Deserialize<JsonElement>(argsJson);
                            if (message.TryGetProperty("{OriginalFormat}", out var formatElement))
                            {
                                var formatString = formatElement.GetString();
                                var formatter = new LogValuesFormatter(formatString);
                                object[] args = new object[formatter.ValueNames.Count];
                                for (int i = 0; i < args.Length; i++)
                                {
                                    args[i] = message.GetProperty(formatter.ValueNames[i]).GetString();
                                }

                                logger.Log(logLevel, new EventId(eventId, eventName), exception, formatString, args);
                            }
                            else
                            {
                                var obj = new LogObject(message, lastFormattedMessage);
                                logger.Log(logLevel, new EventId(eventId, eventName), obj, exception, LogObject.Callback);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Error processing log entry for {ServiceName}", replicaName);
                        }
                    });

                    source.Dynamic.AddCallbackForProviderEvent(MicrosoftExtensionsLoggingProviderName, "FormattedMessage", (traceEvent) =>
                    {
                        // Level, FactoryID, LoggerName, EventID, EventName, FormattedMessage
                        var logLevel = (LogLevel)traceEvent.PayloadByName("Level");
                        var factoryId = (int)traceEvent.PayloadByName("FactoryID");
                        var categoryName = (string)traceEvent.PayloadByName("LoggerName");
                        var eventId = (int)traceEvent.PayloadByName("EventId");
                        var eventName = (string)traceEvent.PayloadByName("EventName");
                        var formattedMessage = (string)traceEvent.PayloadByName("FormattedMessage");

                        if (string.IsNullOrEmpty(formattedMessage))
                        {
                            formattedMessage = eventName;
                            eventName = "";
                        }

                        lastFormattedMessage = formattedMessage;
                    });


                    source.Process();
                }
                catch (DiagnosticsClientException ex)
                {
                    _logger.LogDebug(0, ex, "Failed to start the event pipe session");
                }
                catch (Exception)
                {
                    // This fails if stop is called or if the process dies
                }
                finally
                {
                    session?.Dispose();
                }
            }

            _logger.LogInformation("Event pipe collection completed for {ServiceName} on process id {PID}", replicaName, processId);
        }

        private class ProcessInfo
        {
            public Thread[] Threads { get; set; }

            public CancellationTokenSource StoppedTokenSource { get; set; } = new CancellationTokenSource();
        }
    }
}
