using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            var path = GetExePath(serviceDescription);
            var contentRoot = Path.Combine(Directory.GetCurrentDirectory(), Path.GetDirectoryName(serviceDescription.ProjectFile));
            var environment = new Dictionary<string, string>();
            var args = service.Description.Bindings.Count > 0 ? $"--urls={service.Description.DefaultBinding.Address}" : "";

            service.Status["executablePath"] = path;
            service.Status["workingDir"] = contentRoot;
            service.Status["commandLineArgs"] = args;

            application.PopulateEnvironment(service, (k, v) => environment[k] = v);

            var state = new ProcessState();

            state.Thread = new Thread(() =>
            {
                var restarts = 0;

                while (!state.StoppedTokenSource.IsCancellationRequested)
                {
                    service.State = ServiceState.Starting;
                    service.Status["exitCode"] = null;
                    service.Status["pid"] = null;
                    service.Status["restarts"] = restarts;

                    _logger.LogInformation("Launching service {ServiceName}", serviceName);

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

                                var defaultBinding = service.Description.DefaultBinding;

                                if (defaultBinding == null)
                                {
                                    _logger.LogInformation("{ServiceName} running on process id {PID}", serviceName, pid);
                                }
                                else
                                {
                                    _logger.LogInformation("{ServiceName} running on process id {PID} bound to {Address}", serviceName, pid, defaultBinding.Address);
                                }

                                state.Pid = pid;
                                service.Status["pid"] = pid;
                            },
                            throwOnError: false,
                            cancellationToken: state.StoppedTokenSource.Token);

                        state.ExitCode = result.ExitCode;
                        service.Status["exitCode"] = result.ExitCode;
                        service.State = ServiceState.NotRunning;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(0, ex, "{ServiceName} Failed to launch", serviceName);
                    }
                    finally
                    {
                        restarts++;
                        _logger.LogInformation("{ServiceName} process exited with exit code {ExitCode}", serviceName, state.ExitCode);
                    }
                }
            });

            state.Thread.Start();
            service.Items[typeof(ProcessState)] = state;

            return Task.CompletedTask;
        }

        private Task KillRunningProcesses(IDictionary<string, Service> services)
        {
            void KillProcess(Service service)
            {
                if (service.Items.TryGetValue(typeof(ProcessState), out var stateObj) && stateObj is ProcessState state)
                {
                    try
                    {
                        // Cancel the token before stopping the process
                        state.StoppedTokenSource.Cancel();

                        ProcessUtil.StopProcess(Process.GetProcessById(state.Pid));
                    }
                    catch (Exception)
                    {

                    }
                }
                else if (service.Description.DockerImage != null)
                {
                    Docker.Stop(_logger, service);
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

        private static string GetExePath(ServiceDescription serviceDescription)
        {
            // TODO: Use msbuild to get the target path
            var outputFileName = Path.GetFileNameWithoutExtension(serviceDescription.ProjectFile) + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
            return Path.Combine(Directory.GetCurrentDirectory(), Path.GetDirectoryName(serviceDescription.ProjectFile), "bin", "Debug", "netcoreapp3.1", outputFileName);
        }

        private class ProcessState
        {
            public int Pid { get; set; }

            public Thread Thread { get; set; }

            public CancellationTokenSource StoppedTokenSource { get; set; } = new CancellationTokenSource();

            public int? ExitCode { get; set; }
        }
    }
}
