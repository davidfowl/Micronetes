using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace.Configuration;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace Micronetes
{
    public static class MicronetesServiceCollectionExtensions
    {
        public static IHostBuilder UseMicronetes(this IHostBuilder builder)
        {
            return builder.ConfigureServices(services => services.AddMicronetes());
        }

        private static IServiceCollection AddMicronetes(this IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IClientFactory<>), typeof(DefaultClientFactory<>));
            services.TryAddSingleton<IClientFactory<ConnectionMultiplexer>, StackExchangeRedisClientFactory>();
            services.TryAddSingleton<IClientFactory<IModel>, RabbitMQClientFactory>();
            services.TryAddSingleton<IClientFactory<HttpClient>, HttpClientFactory>();
            services.AddOpenTelemetry((sp, builder) =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var zipkinUri = config.GetUrl("zipkin");

                var env = sp.GetRequiredService<IHostEnvironment>();

                builder.AddRequestCollector();
                builder.AddDependencyCollector();

                if (zipkinUri != null)
                {
                    builder.UseZipkin(o =>
                    {
                        o.ServiceName = env.ApplicationName;
                        o.Endpoint = new Uri(zipkinUri, "api/v2/spans");
                    });
                }
            });
            return services;
        }
    }
}
