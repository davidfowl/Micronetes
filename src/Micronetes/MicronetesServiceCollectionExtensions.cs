using System;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace Micronetes
{
    public static class MicronetesServiceCollectionExtensions
    {
        public static IServiceCollection AddMicronetes(this IServiceCollection services)
        {
            services.AddSingleton(typeof(IClientFactory<>), typeof(DefaultClientFactory<>));
            services.AddSingleton<IClientFactory<PubSubClient>, PubSubClientFactory>();
            services.AddSingleton<IClientFactory<Channel<byte[]>>, ChannelFactory>();
            services.AddSingleton<IClientFactory<HttpClient>, HttpClientFactory>();
            return services;
        }
    }
}
