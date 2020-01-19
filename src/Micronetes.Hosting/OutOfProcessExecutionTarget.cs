using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Micronetes.Hosting.Infrastructure;
using Micronetes.Hosting.Model;
using Microsoft.Extensions.Logging;

namespace Micronetes.Hosting
{
    public class OutOfProcessExecutionTarget : IExecutionTarget
    {
        private readonly ILogger _logger;
        private readonly bool _debugMode;
        private readonly bool _buildProjects;
        private DiagnosticsCollector _diagnosticsCollector;

        public OutOfProcessExecutionTarget(ILogger logger, OutOfProcessOptions options, DiagnosticsCollector diagnosticsCollector)
        {
            _logger = logger;
            _debugMode = options.DebugMode;
            _buildProjects = options.BuildProjects;
            _diagnosticsCollector = diagnosticsCollector;
        }

        public Task StartAsync(Application application)
        {
            var tasks = new Task[application.Services.Count];
            var index = 0;
            foreach (var s in application.Services)
            {
                tasks[index++] = s.Value.Description.External ? Task.CompletedTask : Task.Run(() => LaunchService(application, s.Value));
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
            var applicationName = "";

            if (serviceDescription.Project != null)
            {
                var fullProjectPath = Path.GetFullPath(Path.Combine(application.ContextDirectory, serviceDescription.Project));
                path = GetExePath(fullProjectPath);
                workingDirectory = Path.GetDirectoryName(fullProjectPath);
                // TODO: Requires msbuild
                applicationName = Path.GetFileNameWithoutExtension(fullProjectPath);

                service.Status.ProjectFilePath = fullProjectPath;
            }
            else
            {
                applicationName = Path.GetFileNameWithoutExtension(serviceDescription.Executable);
                path = Path.GetFullPath(Path.Combine(application.ContextDirectory, serviceDescription.Executable));
                workingDirectory = serviceDescription.WorkingDirectory != null ?
                    Path.GetFullPath(Path.Combine(application.ContextDirectory, serviceDescription.WorkingDirectory)) :
                    Path.GetDirectoryName(path);
            }

            // If this is a dll then use dotnet to run it
            if (Path.GetExtension(path) == ".dll")
            {
                applicationName = Path.GetFileNameWithoutExtension(path);
                args = $"\"{path}\" {args}".Trim();
                path = "dotnet";
            }

            service.Status.ExecutablePath = path;
            service.Status.WorkingDirectory = workingDirectory;
            service.Status.Args = args;

            var processInfo = new ProcessInfo
            {
                Threads = new Thread[service.Description.Replicas.Value]
            };

            if (service.Status.ProjectFilePath != null && service.Description.Build && _buildProjects)
            {
                _logger.LogInformation("Building project {ProjectFile}", service.Status.ProjectFilePath);

                service.Logs.OnNext("======================BUILDING====================");

                var buildResult = ProcessUtil.Run("dotnet", $"build \"{service.Status.ProjectFilePath}\" /nologo",
                                                 outputDataReceived: data => service.Logs.OnNext(data),
                                                 throwOnError: false);

                service.Logs.OnNext("");

                if (buildResult.ExitCode != 0)
                {
                    _logger.LogInformation("Building {ProjectFile} failed with exit code {ExitCode}: " + buildResult.StandardError, service.Status.ProjectFilePath, buildResult.ExitCode);
                    return Task.CompletedTask;
                }
            }

            void RunApplication(IEnumerable<(int Port, int BindingPort, string Protocol)> ports)
            {
                var hasPorts = ports.Any();

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

                    foreach (var p in ports)
                    {
                        environment[$"{p.Protocol?.ToUpper() ?? "HTTP"}_PORT"] = p.BindingPort.ToString();
                    }
                }

                while (!processInfo.StoppedTokenSource.IsCancellationRequested)
                {
                    var replica = serviceName + "_" + Guid.NewGuid().ToString().Substring(0, 10).ToLower();
                    var status = new ProcessStatus();
                    service.Replicas[replica] = status;

                    // This isn't your host name
                    environment["APP_INSTANCE"] = replica;

                    status.ExitCode = null;
                    status.Pid = null;
                    status.Environment = environment;

                    if (hasPorts)
                    {
                        status.Ports = ports.Select(p => p.Port);
                    }

                    _logger.LogInformation("Launching service {ServiceName}: {ExePath} {args}", replica, path, args);

                    var metricsTokenSource = CancellationTokenSource.CreateLinkedTokenSource(processInfo.StoppedTokenSource.Token);

                    // This is the thread that will collect diagnostics from the running process
                    // - Logs - I'll collect structured logs from Microsoft.Extensions.Logging
                    // - Metrics - It'll collect EventCounters
                    // - Distribued Traces - It'll create spans
                    var diagnosticsThread = new Thread(state =>
                    {
                        _diagnosticsCollector.ProcessEvents(
                            applicationName,
                            service.Description.Name,
                            (int)state,
                            replica,
                            status,
                            metricsTokenSource.Token);
                    });

                    try
                    {
                        var result = ProcessUtil.Run(path, args,
                            environmentVariables: environment,
                            workingDirectory: workingDirectory,
                            outputDataReceived: data => service.Logs.OnNext("[" + replica + "]: " + data),
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

                                status.Pid = pid;

                                diagnosticsThread.Start(pid);
                            },
                            throwOnError: false,
                            cancellationToken: processInfo.StoppedTokenSource.Token);

                        status.ExitCode = result.ExitCode;

                        if (status.Pid != null)
                        {
                            metricsTokenSource.Cancel();

                            diagnosticsThread.Join();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(0, ex, "Failed to launch process for service {ServiceName}", replica);

                        Thread.Sleep(5000);
                    }

                    service.Restarts++;

                    if (status.ExitCode != null)
                    {
                        _logger.LogInformation("{ServiceName} process exited with exit code {ExitCode}", replica, status.ExitCode);
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
                    var ports = new List<(int, int, string)>();
                    foreach (var binding in serviceDescription.Bindings)
                    {
                        if (binding.Port == null)
                        {
                            continue;
                        }

                        ports.Add((service.PortMap[binding.Port.Value][i], binding.Port.Value, binding.Protocol));
                    }

                    processInfo.Threads[i] = new Thread(() => RunApplication(ports));
                }
            }
            else
            {
                for (int i = 0; i < service.Description.Replicas; i++)
                {
                    processInfo.Threads[i] = new Thread(() => RunApplication(Enumerable.Empty<(int, int, string)>()));
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

            var debugOutputPath = Path.Combine(Path.GetDirectoryName(projectFilePath), "bin", "Debug");

            var tfms = Directory.Exists(debugOutputPath) ? Directory.GetDirectories(debugOutputPath) : Array.Empty<string>();

            if (tfms.Length > 0)
            {
                // Pick the first one
                var path = Path.Combine(debugOutputPath, tfms[0], outputFileName);
                if (File.Exists(path))
                {
                    return path;
                }

                // Older versions of .NET Core didn't have TFMs
                return Path.Combine(debugOutputPath, tfms[0], Path.GetFileNameWithoutExtension(projectFilePath) + ".dll");
            }

            return Path.Combine(debugOutputPath, "netcoreapp3.1", outputFileName);
        }

        private class ProcessInfo
        {
            public Thread[] Threads { get; set; }

            public CancellationTokenSource StoppedTokenSource { get; set; } = new CancellationTokenSource();
        }
    }
}
