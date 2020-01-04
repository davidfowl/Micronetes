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
using Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[assembly: HostingStartup(typeof(Initializer))]

namespace Application
{
    public class Program
    {
        private static string ServicesPath = Path.Combine("obj", "services.json");

        public static Application App = new Application(new[]
            {
                new ServiceDescription {
                    Name = "FrontEnd",
                    HasAddresses = true,
                    Exposed = new List<string> 
                    {
                        "HTTP"
                    }
                },
                new ServiceDescription {
                    Name = "BackEnd",
                    HasAddresses = true,
                    Exposed = new List<string>
                    {
                        "HTTP"
                    }
                },
                new ServiceDescription {
                    Name = "Worker",
                    HasAddresses = false,
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

            if (File.Exists(ServicesPath))
            {
                var old = File.ReadAllText(ServicesPath);
                var oldServices = JsonSerializer.Deserialize<Dictionary<string, Service>>(old);
                KillRunningProcesses(oldServices);
            }

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

            await host.StartAsync();

            try
            {
                // await LaunchInProcess(args);
                await LaunchOutOfProcess(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                var state = JsonSerializer.Serialize(App.Services);
                File.WriteAllText(ServicesPath, state);
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

            KillRunningProcesses(App.Services);
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
                FrontEnd.Program.Main(DefineService(args, "FrontEnd")),
                BackEnd.Program.Main(DefineService(args, "BackEnd")),
                Worker.Program.Main(DefineService(args, "Worker"))
            };

            return Task.CompletedTask;
        }

        private static Task LaunchOutOfProcess(string[] args)
        {
            // Locate executable how?
            var tasks = new Task[App.Services.Count];
            var index = 0;
            foreach (var s in App.Services)
            {
                tasks[index++] = s.Value.Description.External ? Task.CompletedTask : LaunchOne(s.Value, args);
            }

            return Task.WhenAll(tasks);
        }

        private static Task LaunchOne(Service service, string[] args)
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
                try
                {
                    ProcessUtil.Run(path, string.Join(" ", DefineService(args, serviceName)),
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
                                    service.Addresses.Add(line.Substring(addressIndex).Trim());

                                    App.ServiceBound();

                                    tcs.TrySetResult(null);
                                }
                            }

                            service.Logs.Add(data);
                        },
                        onStart: pid =>
                        {
                            service.Pid = pid;
                            if (!serviceDescription.HasAddresses)
                            {
                                tcs.TrySetResult(null);
                            }
                        });
                }
                catch (Exception)
                {

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

        private static string[] DefineService(string[] args, string serviceName)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), serviceName);

            return CombineArgs(args, $"--urls=http://127.0.0.1:0", $"--contentRoot={path}");
        }

        private static string[] CombineArgs(string[] args, params string[] newArgs)
        {
            return args.Concat(newArgs).ToArray();
        }

        public class ServiceDescription
        {
            public string Name { get; set; }
            public bool HasAddresses { get; set; }
            public bool External { get; set; }
            public List<string> Exposed { get; set; } = new List<string>();
        }

        public class Service
        {
            public ServiceDescription Description { get; set; }

            public int? Pid { get; set; }

            public string State => "Running";

            [JsonIgnore]
            public Thread Thread { get; set; }

            [JsonIgnore]
            public List<string> Logs { get; } = new List<string>();

            public List<string> Addresses { get; } = new List<string>();
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

    public class Initializer : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) =>
            {
                services.AddHostedService<BoostrapHostedService>();
            });
        }

        private class BoostrapHostedService : IHostedService
        {
            public BoostrapHostedService(IHostApplicationLifetime lifetime, IServer server, IHostEnvironment environment)
            {
                lifetime.ApplicationStarted.Register(() =>
                {
                    var addresses = server.Features.Get<IServerAddressesFeature>();
                    var app = Program.App;
                    var services = app.Services;
                    if (services.ContainsKey(environment.ApplicationName))
                    {
                        services[environment.ApplicationName].Addresses.AddRange(addresses.Addresses);
                        app.ServiceBound();
                    }
                });
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}
