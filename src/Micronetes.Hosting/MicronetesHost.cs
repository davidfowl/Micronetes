using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Micronetes.Hosting.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Filters;

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
                .UseSerilog((context, configuration)=>
                {
                    configuration
                        .Filter.ByExcluding(Matching.FromSource("Microsoft"))
                        .Enrich
                        .FromLogContext()
                        .WriteTo
                        .Console();
                })
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
            var serverAddressesFeature = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();

            lifetime.ApplicationStopping.Register(() => KillRunningProcesses(application.Services));

            await host.StartAsync();

            logger.LogInformation("API server running on {Addresses}", string.Join(", ", serverAddressesFeature.Addresses));

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

        private static void PopulateEnvironment(Application application, Service service, IDictionary<string, string> environment)
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
    }
}
