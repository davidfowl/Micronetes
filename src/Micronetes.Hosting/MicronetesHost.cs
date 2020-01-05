using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Micronetes.Hosting
{
    public partial class MicronetesHost
    {
        public static async Task RunAsync(Application application, string[] args)
        {
            var options = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            using var host = Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(web =>
                {
                    web.Configure(app =>
                    {
                        app.UseRouting();

                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.Map("/", context =>
                            {
                                context.Response.ContentType = "application/json";
                                return JsonSerializer.SerializeAsync(context.Response.Body, new[]
                                {
                                    $"{context.Request.Scheme}://{context.Request.Host}/api/v1/services",
                                    $"{context.Request.Scheme}://{context.Request.Host}/api/v1/logs/{{service}}",
                                },
                                options);
                            });

                            endpoints.MapGet("/api/v1/services", async context =>
                            {
                                context.Response.ContentType = "application/json";

                                var services = application.Services.OrderBy(s => s.Key).Select(s => s.Value);

                                await JsonSerializer.SerializeAsync(context.Response.Body, services, options);
                            });

                            endpoints.MapGet("/api/v1/services/{name}", async context =>
                            {
                                var name = (string)context.Request.RouteValues["name"];
                                context.Response.ContentType = "application/json";

                                if (!application.Services.TryGetValue(name, out var service))
                                {
                                    context.Response.StatusCode = 404;
                                    await JsonSerializer.SerializeAsync(context.Response.Body, new
                                    {
                                        message = $"Unknown service {name}"
                                    },
                                    options);

                                    return;
                                }

                                await JsonSerializer.SerializeAsync(context.Response.Body, service, options);
                            });

                            endpoints.MapGet("/api/v1/logs/{name}", async context =>
                            {
                                var name = (string)context.Request.RouteValues["name"];
                                context.Response.ContentType = "application/json";

                                if (!application.Services.TryGetValue(name, out var service))
                                {
                                    context.Response.StatusCode = 404;
                                    await JsonSerializer.SerializeAsync(context.Response.Body, new
                                    {
                                        message = $"Unknown service {name}"
                                    },
                                    options);

                                    return;
                                }

                                await JsonSerializer.SerializeAsync(context.Response.Body, service.Logs, options);
                            });
                        });
                    });
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<MicronetesHost>>();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var configuration = host.Services.GetRequiredService<IConfiguration>();

            lifetime.ApplicationStopping.Register(() => KillRunningProcesses(application.Services));

            await host.StartAsync();

            try
            {
                if (args.Contains("--k8s") || args.Contains("--kubernetes"))
                {
                    await LaunchApplcationInK8s(application, logger);
                }
                else
                {
                    await LaunchApplication(application, logger);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(0, ex, "Failed to launch application");
            }

            await host.WaitForShutdownAsync();
        }

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

        private static void SetEnvironment(Application application, Service service, IDictionary<string, string> environment)
        {
            foreach (var s in application.Services.Values)
            {
                if (s == service)
                {
                    continue;
                }

                foreach (var b in s.Description.Bindings)
                {
                    string bindingName;
                    if (b.IsDefault)
                    {
                        bindingName = $"{s.Description.Name.ToUpper()}_SERVICE";
                    }
                    else
                    {
                        bindingName = $"{s.Description.Name.ToUpper()}_{b.Name.ToUpper()}_SERVICE";
                    }
                    environment[bindingName] = b.Address;
                    environment[$"{bindingName}_PROTOCOL"] = b.Protocol;
                }
            }
        }

        private static Task LaunchService(Application application, ILogger logger, Service service)
        {
            var serviceDescription = service.Description;
            var serviceName = serviceDescription.Name;
            var path = GetExePath(serviceName);
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var environment = new Dictionary<string, string>();

            SetEnvironment(application, service, environment);

            var thread = new Thread(() =>
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

            thread.Start();

            service.Thread = thread;

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
