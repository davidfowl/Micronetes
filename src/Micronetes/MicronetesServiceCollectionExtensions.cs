using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
            return services;
        }
    }
}
