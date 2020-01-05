using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Application
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var application = new Application(new[]
            {
                new ServiceDescription {
                    Name = "FrontEnd",
                    Bindings = new List<Binding>
                    {
                        new Binding {
                            Name = "default",
                            Address = "http://localhost:7000",
                            Protocol = "http"
                        }
                    }
                },
                new ServiceDescription {
                    Name = "BackEnd",
                    Bindings = new List<Binding>
                    {
                        new Binding {
                            Name = "default",
                            Address = "http://localhost:8000",
                            Protocol = "http"
                        }
                    }
                },
                new ServiceDescription {
                    Name = "Worker",
                },
                new ServiceDescription {
                    Name = "Redis",
                    External = true,
                    Bindings = new List<Binding>
                    {
                        new Binding {
                            Name = "default",
                            Address = "localhost:6379",
                            Protocol = "redis"
                        }
                    }
                }
            });

            var options = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var host = Host.CreateDefaultBuilder()
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

            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            await host.StartAsync();

            try
            {
                await LaunchApplication(application, logger, args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            AssemblyLoadContext.Default.Unloading += _ =>
            {
                KillRunningProcesses(application.Services);
            };

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                KillRunningProcesses(application.Services);
            };

            using (host)
            {
                await host.WaitForShutdownAsync();
            }
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

        private static Task LaunchApplication(Application application, ILogger logger, string[] args)
        {
            var tasks = new Task[application.Services.Count];
            var index = 0;
            foreach (var s in application.Services)
            {
                tasks[index++] = s.Value.Description.External ? Task.CompletedTask : LaunchService(application, logger, s.Value, args);
            }

            return Task.WhenAll(tasks);
        }

        private static Task LaunchService(Application application, ILogger logger, Service service, string[] args)
        {
            var serviceDescription = service.Description;
            var serviceName = serviceDescription.Name;
            var path = GetExePath(serviceName);
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var environment = new Dictionary<string, string>();

            foreach (var s in application.Services.Values)
            {
                if (s == service)
                {
                    continue;
                }

                foreach (var b in s.Description.Bindings)
                {
                    string bindingName = null;
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

            var thread = new Thread(() =>
            {
                logger.LogInformation("Launching service {ServiceName} on thread {ThreadId}", serviceName, Thread.CurrentThread.ManagedThreadId);

                ProcessResult result = null;
                try
                {
                    result = ProcessUtil.Run(path, string.Join(" ", AddServiceBinding(args, service)),
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
                            logger.LogInformation("{ServiceName} running on process id {PID}", serviceName, pid);

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

        private static string GetExePath(string serviceName)
        {
            // TODO: How do we determine the output path? Assembly attribute compiled in by the build system?
            return Path.Combine(Directory.GetCurrentDirectory(), serviceName, "bin", "Debug", "netcoreapp3.1", serviceName + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : ""));
        }

        private static string[] AddServiceBinding(string[] args, Service service)
        {
            if (service.Description.Bindings.Count > 0)
            {
                var moreArgs = service.Description.Bindings.Where(b => b.IsDefault)
                                                           .Select(a => $"--urls={a.Address}")
                                                           .ToArray();

                return CombineArgs(args, moreArgs);
            }

            return args;
        }

        private static string[] CombineArgs(string[] args, params string[] newArgs)
        {
            return args.Concat(newArgs).ToArray();
        }

        public class Application
        {
            public Application(ServiceDescription[] services)
            {
                Services = services.ToDictionary(s => s.Name, s => new Service { Description = s });
            }

            public Dictionary<string, Service> Services { get; }
        }

        public class Service
        {
            public ServiceDescription Description { get; set; }

            public int? Pid { get; set; }

            public string State => ExitCode == null ? "Running" : "Stopped";

            [JsonIgnore]
            public Thread Thread { get; set; }

            [JsonIgnore]
            public List<string> Logs { get; } = new List<string>();

            public int? ExitCode { get; set; }
        }

        public class ServiceDescription
        {
            public string Name { get; set; }
            public bool External { get; set; }
            public List<Binding> Bindings { get; set; } = new List<Binding>();
        }

        public class Binding
        {
            public string Name { get; set; }
            public string Address { get; set; }
            public string Protocol { get; set; }

            internal bool IsDefault => Name == "default";
        }
    }
}
