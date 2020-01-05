using System;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace Micronetes
{
    public static class MicronetesServiceCollectionExtensions
    {
        public static IServiceCollection AddMicronetes(this IServiceCollection services)
        {
            services.AddSingleton(typeof(IClientFactory<>), typeof(DefaultClientFactory<>));
            services.AddSingleton<IClientFactory<ConnectionMultiplexer>, StackExchangeRedisClientFactory>();
            services.AddSingleton<IClientFactory<IModel>, RabbitMQClientFactory>();
            services.AddSingleton<IClientFactory<HttpClient>, HttpClientFactory>();
            return services;
        }
    }
}
