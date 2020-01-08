using System;
using System.Collections.Generic;
using System.IO;
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
            var environment = new Dictionary<string, string>();

            service.Status["projectFilePath"] = fullProjectPath;
            service.Status["executablePath"] = path;
            service.Status["workingDir"] = contentRoot;

            application.PopulateEnvironment(service, (k, v) => environment[k] = v);

            var state = new ProcessState
            {
                Threads = new Thread[service.Description.Replicas.Value]
            };

            void RunApplication(int? port)
            {
                var args = port != null ? $"--urls=http://localhost:{port}" : "";
                var restarts = 0;

                while (!state.StoppedTokenSource.IsCancellationRequested)
                {
                    var replica = serviceName + "_" + Guid.NewGuid().ToString().Substring(0, 10).ToLower();
                    var status = service.Replicas[replica] = new ServiceReplica();

                    service.State = ServiceState.Starting;
                    status["exitCode"] = null;
                    status["pid"] = null;
                    status["commandLineArgs"] = args;

                    if(port != null)
                    {
                        status["port"] = port;
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

                                service.Logs.Add(data);
                            },
                            onStart: pid =>
                            {
                                service.State = ServiceState.Running;

                                if (port == null)
                                {
                                    _logger.LogInformation("{ServiceName} running on process id {PID}", replica, pid);
                                }
                                else
                                {
                                    _logger.LogInformation("{ServiceName} running on process id {PID} bound to {Address}", replica, pid, $"http://localhost:{port}");
                                }

                                status["pid"] = pid;
                            },
                            throwOnError: false,
                            cancellationToken: state.StoppedTokenSource.Token);

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

            var defaultBinding = service.Description.DefaultBinding;

            if (defaultBinding != null)
            {
                var ports = service.PortMap[defaultBinding.Port.Value];
                for (int i = 0; i < service.Description.Replicas; i++)
                {
                    var port = ports[i];
                    state.Threads[i] = new Thread(() => RunApplication(port));
                }
            }
            else
            {
                for (int i = 0; i < service.Description.Replicas; i++)
                {
                    state.Threads[i] = new Thread(() => RunApplication(null));
                }
            }

            for (int i = 0; i < service.Description.Replicas; i++)
            {
                state.Threads[i].Start();
            }

            service.Items[typeof(ProcessState)] = state;

            return Task.CompletedTask;
        }

        private Task KillRunningProcesses(IDictionary<string, Service> services)
        {
            static void KillProcess(Service service)
            {
                if (service.Items.TryGetValue(typeof(ProcessState), out var stateObj) && stateObj is ProcessState state)
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

        private class ProcessState
        {
            public Thread[] Threads { get; set; }

            public CancellationTokenSource StoppedTokenSource { get; set; } = new CancellationTokenSource();
        }
    }
}
