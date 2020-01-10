using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace.Configuration;
using RabbitMQ.Client;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

namespace Micronetes
{
    public static class MicronetesServiceCollectionExtensions
    {
        public static IHostBuilder UseMicronetes(this IHostBuilder builder)
        {
            return builder.ConfigureServices(services => services.AddMicronetes())
                          .UseSerilog((context, config) =>
                          {
                              config.MinimumLevel.Debug()
                              .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                              .Enrich.FromLogContext()
                              .WriteTo.Console()
                              .WriteTo.Elasticsearch();
                          });
        }

        private static IServiceCollection AddMicronetes(this IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IClientFactory<>), typeof(DefaultClientFactory<>));
            services.TryAddSingleton<IClientFactory<ConnectionMultiplexer>, StackExchangeRedisClientFactory>();
            services.TryAddSingleton<IClientFactory<PubSubClient>, StackExchangeRedisPubSubClientFactory>();
            services.TryAddSingleton<IClientFactory<IModel>, RabbitMQClientFactory>();
            services.TryAddSingleton<IClientFactory<HttpClient>, HttpClientFactory>();
            services.AddOpenTelemetry((sp, builder) =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var zipkinUrl = config.GetUrl("zipkin");

                var env = sp.GetRequiredService<IHostEnvironment>();

                builder.AddRequestCollector();

                if (!string.IsNullOrEmpty(zipkinUrl))
                {
                    builder.UseZipkin(o =>
                    {
                        o.ServiceName = env.ApplicationName;
                        o.Endpoint = new Uri(zipkinUrl + "api/v2/spans");
                    });
                }
            });
            return services;
        }
    }
}
