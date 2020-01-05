using System;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace Micronetes
{
    public static class MicronetesServiceCollectionExtensions
    {
        public static IServiceCollection AddMicronetes(this IServiceCollection services)
        {
            services.TryAddSingleton(typeof(IClientFactory<>), typeof(DefaultClientFactory<>));
            services.TryAddSingleton<IClientFactory<ConnectionMultiplexer>, StackExchangeRedisClientFactory>();
            services.TryAddSingleton<IClientFactory<IModel>, RabbitMQClientFactory>();
            services.TryAddSingleton<IClientFactory<HttpClient>, HttpClientFactory>();
            services.TryAddSingleton<INameResolver, ConfigurationNameResolver>();
            return services;
        }
    }
}
