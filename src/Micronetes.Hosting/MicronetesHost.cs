using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Micronetes.Hosting.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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
                    });

                    web.Configure(app =>
                    {
                        app.UseDeveloperExceptionPage();

                        app.UseStaticFiles();

                        app.UseRouting();

                        var api = new MicronetesApi();

                        app.UseEndpoints(endpoints =>
                        {
                            api.MapRoutes(endpoints);

                            endpoints.MapBlazorHub();
                            endpoints.MapFallbackToPage("/_Host");
                        });
                    });
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<MicronetesHost>>();

            logger.LogInformation("Executing application from  {Source}", application.Source);

            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            var serverAddressesFeature = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();

            var diagnosticOptions = DiagnosticOptions.FromConfiguration(configuration);
            var diagnosticsCollector = new DiagnosticsCollector(logger, diagnosticOptions);

            // Print out what providers were selected and their values
            diagnosticOptions.DumpDiagnostics(logger);

            var processor = new AggregateApplicationProcessor(new IApplicationProcessor[] {
                new ProxyService(logger),
                new DockerRunner(logger),
                new ProcessRunner(logger, ProcessRunnerOptions.FromArgs(args), diagnosticsCollector),
            });

            await host.StartAsync();

            logger.LogInformation("Dashboard running on {Address}", serverAddressesFeature.Addresses.First());

            try
            {
                await processor.StartAsync(application);
            }
            catch (Exception ex)
            {
                logger.LogError(0, ex, "Failed to launch application");
            }

            var waitForStop = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            lifetime.ApplicationStopping.Register(obj => waitForStop.TrySetResult(null), null);

            await waitForStop.Task;

            logger.LogInformation("Shutting down...");

            try
            {
                await processor.StopAsync(application);
            }
            finally
            {
                // Stop the host after everything else has been shutdown
                await host.StopAsync();
            }
        }
    }
}
