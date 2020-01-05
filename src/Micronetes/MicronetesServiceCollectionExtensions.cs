using System;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace Micronetes
{
    public static class MicronetesServiceCollectionExtensions
    {
        private static string ServiceAccountPath = Path.Combine("var", "run", "secrets", "kubernetes.io", "serviceaccount/");
        private const string ServiceAccountTokenKeyFileName = "token";
        private const string ServiceAccountRootCAKeyFileName = "ca.crt";

        public static IServiceCollection AddMicronetes(this IServiceCollection services)
        {
            services.AddSingleton(typeof(IClientFactory<>), typeof(DefaultClientFactory<>));

            if (IsInCluster())
            {
                services.AddSingleton<IClientFactory<PubSubClient>, HttpPubSubClientFactory>();
                services.AddSingleton<IClientFactory<Channel<byte[]>>, HttpChannelFactory>();
                services.AddSingleton<IClientFactory<HttpClient>, KubenetesHttpClientFactory>();
            }
            else
            {
                services.AddSingleton<IClientFactory<PubSubClient>, PubSubClientFactory>();
                services.AddSingleton<IClientFactory<Channel<byte[]>>, ChannelFactory>();
                services.AddSingleton<IClientFactory<HttpClient>, HttpClientFactory>();
            }
            return services;
        }

        private static bool IsInCluster()
        {
            var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
            var port = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(port))
            {
                return false;
            }
            var tokenPath = Path.Combine(ServiceAccountPath, ServiceAccountTokenKeyFileName);
            if (!File.Exists(tokenPath))
            {
                return false;
            }
            var certPath = Path.Combine(ServiceAccountPath, ServiceAccountRootCAKeyFileName);
            return File.Exists(certPath);
        }
    }
}
