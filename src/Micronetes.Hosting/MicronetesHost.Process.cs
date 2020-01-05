using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Micronetes.Hosting.Model;
using Microsoft.Extensions.Logging;

namespace Micronetes.Hosting
{
    public partial class MicronetesHost
    {
        private static Task LaunchApplication(Application application, ILogger logger)
        {
            var tasks = new Task[application.Services.Count];
            var index = 0;
            foreach (var s in application.Services)
            {
                tasks[index++] = s.Value.Description.External ? Task.CompletedTask : LaunchService(application, logger, s.Value);
            }

            return Task.WhenAll(tasks);
        }

        private static Task LaunchService(Application application, ILogger logger, Service service)
        {
            var serviceDescription = service.Description;
            var serviceName = serviceDescription.Name;
            var path = GetExePath(serviceName);
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var environment = new Dictionary<string, string>();

            PopulateEnvironment(application, service, environment);

            service.Thread = new Thread(() =>
            {
                logger.LogInformation("Launching service {ServiceName}", serviceName);

                try
                {
                    var result = ProcessUtil.Run(path, GetServiceBindingArgs(service),
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
                                logger.LogInformation("{ServiceName} running on process id {PID}", serviceName, pid);
                            }
                            else
                            {
                                logger.LogInformation("{ServiceName} running on process id {PID} bound to {Address}", serviceName, pid, defaultBinding.Address);
                            }

                            service.Pid = pid;

                            tcs.TrySetResult(null);
                        },
                        throwOnError: false);

                    service.ExitCode = result.ExitCode;
                }
                catch (Exception ex)
                {
                    logger.LogError(0, ex, "{ServiceName} Failed to launch", serviceName);
                }
                finally
                {
                    logger.LogInformation("{ServiceName} process exited", serviceName);

                    tcs.TrySetResult(null);
                }
            });

            service.Thread.Start();

            return tcs.Task;
        }

        private static void KillRunningProcesses(IDictionary<string, Service> services)
        {
            static void KillProcess(int? pid)
            {
                if (pid == null)
                {
                    return;
                }

                try
                {
                    ProcessUtil.StopProcess(Process.GetProcessById(pid.Value));
                }
                catch (Exception)
                {

                }
            }

            var index = 0;
            var tasks = new Task[services.Count];
            foreach (var s in services.Values)
            {
                var pid = s.Pid;
                tasks[index++] = Task.Run(() => KillProcess(pid));
            }

            Task.WaitAll(tasks);
        }

        private static string GetExePath(string serviceName)
        {
            // TODO: How do we determine the output path? Assembly attribute compiled in by the build system?
            return Path.Combine(Directory.GetCurrentDirectory(), serviceName, "bin", "Debug", "netcoreapp3.1", serviceName + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : ""));
        }

        private static string GetServiceBindingArgs(Service service)
        {
            if (service.Description.Bindings.Count > 0)
            {
                var moreArgs = service.Description.Bindings.Where(b => b.IsDefault)
                                                           .Select(a => $"--urls={a.Address}");

                return $"--urls={service.Description.DefaultBinding.Address}";
            }

            return string.Empty;
        }
    }
}
