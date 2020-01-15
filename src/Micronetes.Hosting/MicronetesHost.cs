using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Micronetes.Hosting.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            };

            options.Converters.Add(ServiceReplica.JsonConverter);

            using var host = Host.CreateDefaultBuilder(args)
                .UseSerilog((context, configuration) =>
                {
                    // Logging for this application
                    configuration
                        .MinimumLevel.Verbose()
                        .Filter.ByExcluding(Matching.FromSource("Microsoft"))
                        .Enrich
                        .FromLogContext()
                        .WriteTo
                        .Console();
                })
                .ConfigureWebHostDefaults(web =>
                {
                    web.ConfigureServices(services =>
                    {
                        services.AddRazorPages();
                        services.AddServerSideBlazor();

                        services.AddOptions<StaticFileOptions>()
                            .PostConfigure(o =>
                            {
                                // Make sure we don't remove the other file providers (blazor needs this)
                                o.FileProvider = new CompositeFileProvider(o.FileProvider, new ManifestEmbeddedFileProvider(typeof(MicronetesHost).Assembly, "wwwroot"));
                            });

                        services.AddSingleton(application);
                    });

                    web.ConfigureKestrel(options =>
                    {
                        var logger = options.ApplicationServices.GetRequiredService<ILogger<MicronetesHost>>();
                        var config = options.ApplicationServices.GetRequiredService<IConfiguration>();

                        if (config["port"] != null && int.TryParse(config["port"], out int cpPort))
                        {
                            // Use the specified port
                            options.Listen(IPAddress.Loopback, cpPort);
                        }
                        else
                        {
                            // This is lame but it allows running multiple versions of this
                            // we should also allow ports to be specified as input
                            options.Listen(IPAddress.Loopback, 0);
                        }

                        foreach (var service in application.Services.Values)
                        {
                            if (service.Description.External)
                            {
                                // We eventually want to proxy everything, this is temporary
                                continue;
                            }

                            static int GetNextPort()
                            {
                                // Let the OS assign the next available port. Unless we cycle through all ports
                                // on a test run, the OS will always increment the port number when making these calls.
                                // This prevents races in parallel test runs where a test is already bound to
                                // a given port, and a new test is able to bind to the same port due to port
                                // reuse being enabled by default by the OS.
                                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                                return ((IPEndPoint)socket.LocalEndPoint).Port;
                            }

                            foreach (var binding in service.Description.Bindings)
                            {
                                if (binding.Port == null)
                                {
                                    continue;
                                }

                                if (service.Description.Replicas == 1)
                                {
                                    // No need to proxy
                                    service.PortMap[binding.Port.Value] = new List<int> { binding.Port.Value };
                                    continue;
                                }

                                var ports = new List<int>();

                                for (int i = 0; i < service.Description.Replicas; i++)
                                {
                                    // Reserve a port for each replica
                                    var port = GetNextPort();
                                    ports.Add(port);
                                }

                                logger.LogInformation("Mapping external port {ExternalPort} to internal port(s) {InternalPorts} for {ServiceName}", binding.Port, string.Join(", ", ports.Select(p => p.ToString())), service.Description.Name);

                                service.PortMap[binding.Port.Value] = ports;

                                options.Listen(IPAddress.Loopback, binding.Port.Value, o =>
                                {
                                    long count = 0;

                                    // o.UseConnectionLogging("Micronetes.Proxy");

                                    o.Run(async connection =>
                                    {
                                        var notificationFeature = connection.Features.Get<IConnectionLifetimeNotificationFeature>();

                                        var next = (int)(Interlocked.Increment(ref count) % ports.Count);

                                        NetworkStream targetStream = null;

                                        try
                                        {
                                            var target = new Socket(SocketType.Stream, ProtocolType.Tcp)
                                            {
                                                NoDelay = true
                                            };
                                            var port = ports[next];

                                            logger.LogDebug("Attempting to connect to {ServiceName} listening on {ExternalPort}:{Port}", service.Description.Name, binding.Port, port);

                                            await target.ConnectAsync(IPAddress.Loopback, port);

                                            logger.LogDebug("Successfully connected to {ServiceName} listening on {ExternalPort}:{Port}", service.Description.Name, binding.Port, port);

                                            targetStream = new NetworkStream(target, ownsSocket: true);
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.LogDebug(ex, "Proxy error for service {ServiceName}", service.Description.Name);

                                            await targetStream.DisposeAsync();

                                            connection.Abort();
                                            return;
                                        }

                                        try
                                        {
                                            logger.LogDebug("Proxying traffic to {ServiceName} {ExternalPort}:{InternalPort}", service.Description.Name, binding.Port, ports[next]);

                                            // external -> internal
                                            var reading = Task.Run(() => connection.Transport.Input.CopyToAsync(targetStream, notificationFeature.ConnectionClosedRequested));

                                            // internal -> external
                                            var writing = Task.Run(() => targetStream.CopyToAsync(connection.Transport.Output, notificationFeature.ConnectionClosedRequested));

                                            await Task.WhenAll(reading, writing);
                                        }
                                        catch (ConnectionResetException)
                                        {
                                            // Connection was reset
                                        }
                                        catch (OperationCanceledException ex)
                                        {
                                            if (!notificationFeature.ConnectionClosedRequested.IsCancellationRequested)
                                            {
                                                logger.LogDebug(0, ex, "Proxy error for service {ServiceName}", service.Description.Name);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.LogDebug(0, ex, "Proxy error for service {ServiceName}", service.Description.Name);
                                        }
                                        finally
                                        {
                                            await targetStream.DisposeAsync();
                                        }

                                        // This needs to reconnect to the target port(s) until its bound
                                        // it has to stop if the service is no longer running
                                    });
                                });
                            }
                        }
                    });

                    web.Configure(app =>
                    {
                        app.UseDeveloperExceptionPage();

                        app.UseStaticFiles();

                        app.UseRouting();

                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.Map("/api/v1", context =>
                            {
                                context.Response.ContentType = "application/json";
                                return JsonSerializer.SerializeAsync(context.Response.Body, new[]
                                {
                                    $"{context.Request.Scheme}://{context.Request.Host}/api/v1/services",
                                    $"{context.Request.Scheme}://{context.Request.Host}/api/v1/logs/{{service}}",
                                    $"{context.Request.Scheme}://{context.Request.Host}/api/v1/metrics",
                                    $"{context.Request.Scheme}://{context.Request.Host}/api/v1/metrics/{{service}}",
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

                                await JsonSerializer.SerializeAsync(context.Response.Body, service.CachedLogs, options);
                            });

                            endpoints.MapGet("/api/v1/metrics", async context =>
                            {
                                var sb = new StringBuilder();
                                foreach (var s in application.Services.OrderBy(s => s.Key))
                                {
                                    sb.AppendLine($"# {s.Key}");
                                    foreach (var replica in s.Value.Replicas)
                                    {
                                        foreach (var metric in replica.Value.Metrics)
                                        {
                                            sb.Append(metric.Key);
                                            sb.Append("{");
                                            sb.Append($"service=\"{s.Key}\",");
                                            sb.Append($"instance=\"{replica.Key}\"");
                                            sb.Append("}");
                                            sb.Append(" ");
                                            sb.Append(metric.Value);
                                            sb.AppendLine();
                                        }
                                    }
                                    sb.AppendLine();
                                }

                                await context.Response.WriteAsync(sb.ToString());
                            });

                            endpoints.MapGet("/api/v1/metrics/{name}", async context =>
                            {
                                var sb = new StringBuilder();

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

                                foreach (var replica in service.Replicas)
                                {
                                    foreach (var metric in replica.Value.Metrics)
                                    {
                                        sb.Append(metric.Key);
                                        sb.Append("{");
                                        sb.Append($"instance=\"{replica.Key}\"");
                                        sb.Append("}");
                                        sb.Append(" ");
                                        sb.Append(metric.Value);
                                        sb.AppendLine();
                                    }
                                }

                                await context.Response.WriteAsync(sb.ToString());
                            });

                            endpoints.MapBlazorHub();
                            endpoints.MapFallbackToPage("/_Host");
                        });
                    });
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<MicronetesHost>>();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            var serverAddressesFeature = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var target = new OutOfProcessExecutionTarget(logger, args.Contains("--debug"));

            // This is the logger factory for application logs. It allows re-routing event pipe collected logs (structured logs)
            // to any of the supported sinks, currently (elastic search and app insights)
            application.LoggerFactory = LoggerFactory.Create(builder =>
            {
                var logs = configuration["logs"];

                if (logs == null)
                {
                    return;
                }

                var pair = logs.Split('=');

                if (pair.Length < 2)
                {
                    return;
                }

                if (string.Equals(pair[0], "elastic", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(pair[1]))
                {

                    var loggerConfiguration = new LoggerConfiguration()
                                                .MinimumLevel.Verbose()
                                                .Enrich.FromLogContext();

                    logger.LogInformation("Using ElasticSearch at {URL}", pair[1]);
                    loggerConfiguration.WriteTo.Elasticsearch(pair[1]);

                    builder.AddSerilog(loggerConfiguration.CreateLogger());
                }

                if (string.Equals(pair[0], "ai", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(pair[1]))
                {
                    logger.LogInformation("Using ApplicationInsights instrumentation key {InstrumentationKey}", pair[1]);
                    builder.AddApplicationInsights(pair[1]);
                }
            });


            await host.StartAsync();

            logger.LogInformation("Dashboard running on {Address}", serverAddressesFeature.Addresses.First());

            try
            {
                await target.StartAsync(application);
            }
            catch (Exception ex)
            {
                logger.LogError(0, ex, "Failed to launch application");
            }

            try
            {
                await host.WaitForShutdownAsync();

                logger.LogInformation("Shutting down...");
            }
            finally
            {
                await target.StopAsync(application);
            }
        }
    }
}
