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

        public OutOfProcessExecutionTarget(ILogger logger)
        {
            _logger = logger;
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
            var fullProjectPath = Path.GetFullPath(Path.Combine(application.ContextDirectory, serviceDescription.ProjectFile));
            var path = GetExePath(fullProjectPath);
            var contentRoot = Path.GetDirectoryName(fullProjectPath);

            service.Status["projectFilePath"] = fullProjectPath;
            service.Status["executablePath"] = path;
            service.Status["workingDir"] = contentRoot;

            var processInfo = new ProcessInfo
            {
                Threads = new Thread[service.Description.Replicas.Value]
            };

            void RunApplication(IEnumerable<int> ports)
            {
                var hasPorts = ports.Any();
                var args = hasPorts ? string.Join(" ", ports.Select(p => $"--urls=http://localhost:{p}")) : "";
                var restarts = 0;

                var environment = new Dictionary<string, string>();
                application.PopulateEnvironment(service, (k, v) => environment[k] = v);

                while (!processInfo.StoppedTokenSource.IsCancellationRequested)
                {
                    var replica = serviceName + "_" + Guid.NewGuid().ToString().Substring(0, 10).ToLower();
                    var status = service.Replicas[replica] = new ServiceReplica();
                    // This isn't your host name
                    environment["APP_INSTANCE"] = replica;

                    service.State = ServiceState.Starting;
                    status["exitCode"] = null;
                    status["pid"] = null;
                    status["commandLineArgs"] = args;

                    if (hasPorts)
                    {
                        status["ports"] = ports;
                    }

                    service.Status["restarts"] = restarts;

                    _logger.LogInformation("Launching service {ServiceName} from {ExePath}", replica, path);

                    try
                    {
                        var result = ProcessUtil.Run(path, args,
                            environmentVariables: environment,
                            workingDirectory: contentRoot,
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
                                service.State = ServiceState.Running;

                                if (hasPorts)
                                {
                                    _logger.LogInformation("{ServiceName} running on process id {PID} bound to {Address}", replica, pid, string.Join(", ", ports.Select(p => p.ToString())));
                                }
                                else
                                {
                                    _logger.LogInformation("{ServiceName} running on process id {PID}", replica, pid);
                                }

                                status["pid"] = pid;
                            },
                            throwOnError: false,
                            cancellationToken: processInfo.StoppedTokenSource.Token);

                        status["exitCode"] = result.ExitCode;
                        service.State = ServiceState.NotRunning;

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
                    var ports = new List<int>();
                    foreach (var binding in serviceDescription.Bindings)
                    {
                        if (binding.Port == null)
                        {
                            continue;
                        }

                        ports.Add(service.PortMap[binding.Port.Value][i]);
                    }

                    processInfo.Threads[i] = new Thread(() => RunApplication(ports));
                }
            }
            else
            {
                for (int i = 0; i < service.Description.Replicas; i++)
                {
                    processInfo.Threads[i] = new Thread(() => RunApplication(Enumerable.Empty<int>()));
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

        private class ProcessInfo
        {
            public Thread[] Threads { get; set; }

            public CancellationTokenSource StoppedTokenSource { get; set; } = new CancellationTokenSource();
        }
    }
}
