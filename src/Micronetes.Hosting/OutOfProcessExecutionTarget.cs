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
            var path = GetExePath(serviceName);
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var environment = new Dictionary<string, string>();
            var args = service.Description.Bindings.Count > 0 ? $"--urls={service.Description.DefaultBinding.Address}" : "";

            application.PopulateEnvironment(service, (k, v) => environment[k] = v);

            var state = new ProcessState();

            state.Thread = new Thread(() =>
            {
                _logger.LogInformation("Launching service {ServiceName}", serviceName);

                try
                {
                    var result = ProcessUtil.Run(path, args,
                        environmentVariables: environment,
                        workingDirectory: Path.Combine(Directory.GetCurrentDirectory(), serviceName),
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

                            tcs.TrySetResult(null);
                        },
                        throwOnError: false);

                    state.ExitCode = result.ExitCode;
                }
                catch (Exception ex)
                {
                    _logger.LogError(0, ex, "{ServiceName} Failed to launch", serviceName);
                }
                finally
                {
                    _logger.LogInformation("{ServiceName} process exited with exit code {ExitCode}", serviceName, state.ExitCode);

                    tcs.TrySetResult(null);
                }
            });

            state.Thread.Start();
            service.Items[typeof(ProcessState)] = state;

            return tcs.Task;
        }

        private Task KillRunningProcesses(IDictionary<string, Service> services)
        {
            void KillProcess(Service service)
            {
                if (service.Items.TryGetValue(typeof(ProcessState), out var stateObj) && stateObj is ProcessState state)
                {
                    try
                    {
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

        private static string GetExePath(string serviceName)
        {
            // TODO: How do we determine the output path? Assembly attribute compiled in by the build system?
            return Path.Combine(Directory.GetCurrentDirectory(), serviceName, "bin", "Debug", "netcoreapp3.1", serviceName + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : ""));
        }

        private class ProcessState
        {
            public int Pid { get; set; }

            public Thread Thread { get; set; }

            public int? ExitCode { get; set; }
        }
    }
}
