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
        private static string ServicesPath = Path.Combine("obj", "services.json");

        public static Application App = new Application(new[]
            {
                new ServiceDescription {
                    Name = "FrontEnd",
                    Addresses = new List<string>
                    {
                        "http://localhost:7000",
                    },
                    Exposed = new List<string>
                    {
                        "HTTP"
                    }
                },
                new ServiceDescription {
                    Name = "BackEnd",
                    Addresses = new List<string>
                    {
                        "http://localhost:8000",
                    },
                    Exposed = new List<string>
                    {
                        "HTTP"
                    }
                },
                new ServiceDescription {
                    Name = "Worker",
                },
                new ServiceDescription {
                    Name = "MQ",
                    External = true,
                    Exposed = new List<string>
                    {
                        "Queue"
                    }
                }
            });

        public static async Task Main(string[] args)
        {
            var options = new JsonSerializerOptions()
            {

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
                                await App.Intialized;

                                context.Response.ContentType = "application/json";

                                await JsonSerializer.SerializeAsync(context.Response.Body, App.Services, options);
                            });

                            endpoints.MapGet("/api/v1/services/{name}", async context =>
                             {
                                 await App.Intialized;

                                 var name = (string)context.Request.RouteValues["name"];
                                 context.Response.ContentType = "application/json";

                                 if (!App.Services.TryGetValue(name, out var service))
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
                                await App.Intialized;

                                var name = (string)context.Request.RouteValues["name"];
                                context.Response.ContentType = "application/json";

                                if (!App.Services.TryGetValue(name, out var service))
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
                // await LaunchInProcess(args);
                await LaunchOutOfProcess(logger, args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            AssemblyLoadContext.Default.Unloading += _ =>
            {
                KillRunningProcesses(App.Services);
            };

            try
            {
                using (host)
                {
                    await host.WaitForShutdownAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                KillRunningProcesses(App.Services);
            }

        }

        private static void KillRunningProcesses(IDictionary<string, Service> services)
        {
            foreach (var s in services.Values)
            {
                if (s.Pid == null)
                {
                    continue;
                }

                try
                {
                    ProcessUtil.StopProcess(Process.GetProcessById(s.Pid.Value));
                }
                catch (Exception)
                {

                }
            }
        }

        private static Task LaunchInProcess(string[] args)
        {
            // Not needed for in process but helps with debugging
            // Environment.SetEnvironmentVariable("API_SERVER", "http://localhost:5000");
            // Environment.SetEnvironmentVariable("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "Application");

            var tasks = new[]
            {
                FrontEnd.Program.Main(DefineService(args, App.Services["FrontEnd"])),
                BackEnd.Program.Main(DefineService(args, App.Services["BackEnd"])),
                Worker.Program.Main(DefineService(args, App.Services["Worker"]))
            };

            return Task.CompletedTask;
        }

        private static Task LaunchOutOfProcess(ILogger logger, string[] args)
        {
            // Locate executable how?
            var tasks = new Task[App.Services.Count];
            var index = 0;
            foreach (var s in App.Services)
            {
                tasks[index++] = s.Value.Description.External ? Task.CompletedTask : LaunchService(logger, s.Value, args);
            }

            return Task.WhenAll(tasks);
        }

        private static Task LaunchService(ILogger logger, Service service, string[] args)
        {
            var serviceDescription = service.Description;
            var serviceName = serviceDescription.Name;
            var path = GetExePath(serviceName);
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var env = new Dictionary<string, string>
            {
                { "API_SERVER", "https://localhost:5001" }
            };

            var thread = new Thread(() =>
            {
                logger.LogInformation("Launching service {ServiceName} on thread {ThreadId}", serviceName, Thread.CurrentThread.ManagedThreadId);

                ProcessResult result = null;
                try
                {
                    result = ProcessUtil.Run(path, string.Join(" ", DefineService(args, service)),
                        environmentVariables: env,
                        workingDirectory: Path.Combine(Directory.GetCurrentDirectory(), serviceName),
                        outputDataReceived: data =>
                        {
                            if (data == null)
                            {
                                return;
                            }

                            if (serviceDescription.HasAddresses)
                            {
                                // Now listening on: "{url}"
                                var line = data.Trim();
                                if (line.StartsWith("Now listening on") && line.IndexOf("http") >= 0)
                                {
                                    var addressIndex = line.IndexOf("http");

                                    App.ServiceBound();

                                    tcs.TrySetResult(null);

                                    logger.LogInformation("{ServiceName} bound", serviceName);
                                }
                            }

                            service.Logs.Add(data);
                        },
                        onStart: pid =>
                        {
                            logger.LogInformation("{ServiceName} running on process id {PID}", serviceName, pid);

                            service.Pid = pid;

                            if (!serviceDescription.HasAddresses)
                            {
                                tcs.TrySetResult(null);
                            }
                        },
                        throwOnError: false);

                    service.ExitCode = result.ExitCode;
                }
                catch (Exception)
                {

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
            return Path.Combine(Directory.GetCurrentDirectory(), serviceName, "bin", "Debug", "netcoreapp3.1", serviceName + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : ""));
        }

        private static string[] DefineService(string[] args, Service service)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), service.Description.Name);

            if (service.Description.HasAddresses)
            {
                var moreArgs = service.Description.Addresses.Select(a => $"--urls={a}");

                var s = args.Concat(new[] { $"--contentRoot={path}" }).Concat(moreArgs).ToArray();
                return s;
            }

            return CombineArgs(args, $"--contentRoot={path}");
        }

        private static string[] CombineArgs(string[] args, params string[] newArgs)
        {
            return args.Concat(newArgs).ToArray();
        }

        public class ServiceDescription
        {
            public string Name { get; set; }
            public bool HasAddresses => Addresses.Count > 0;
            public bool External { get; set; }
            public List<string> Exposed { get; set; } = new List<string>();
            public List<string> Addresses { get; set; } = new List<string>();
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

        public class Application
        {
            private int _bindableServices;
            private readonly TaskCompletionSource<object> _tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Application(ServiceDescription[] services)
            {
                foreach (var s in services)
                {
                    Services[s.Name] = new Service
                    {
                        Pid = s.External ? (int?)null : Process.GetCurrentProcess().Id,
                        Description = s
                    };

                    if (s.HasAddresses)
                    {
                        _bindableServices++;
                    }
                }
            }

            public ConcurrentDictionary<string, Service> Services { get; } = new ConcurrentDictionary<string, Service>();

            public Task Intialized => _tcs.Task;

            public void ServiceBound()
            {
                if (Interlocked.Decrement(ref _bindableServices) == 0)
                {
                    _tcs.TrySetResult(null);
                }
            }
        }
    }
}
