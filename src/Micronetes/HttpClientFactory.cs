using System;
using System.Collections.Concurrent;
using System.Net.Http;

namespace Micronetes
{
    public class HttpClientFactory : IClientFactory<HttpClient>
    {
        private readonly ConcurrentDictionary<string, HttpClient> _clients = new ConcurrentDictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);
        private readonly HttpServiceRegistry _httpServiceRegistry;

        public HttpClientFactory(HttpServiceRegistry httpServiceRegistry)
        {
            _httpServiceRegistry = httpServiceRegistry;
        }

        public HttpClient CreateClient(string name)
        {
            // REVIEW: Settings options configuration from where?
            if (!_httpServiceRegistry.TryGetValue(name, out var serviceUrl))
            {
                throw new InvalidOperationException($"No such http service {name}");
            }

            return _clients.GetOrAdd(name, k => new HttpClient()
            {
                BaseAddress = new Uri(serviceUrl)
            });
        }
    }
}
