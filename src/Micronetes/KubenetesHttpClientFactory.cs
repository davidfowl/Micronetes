using System;
using System.Collections.Concurrent;
using System.Net.Http;

namespace Micronetes
{
    public class KubenetesHttpClientFactory : IClientFactory<HttpClient>
    {
        private readonly ConcurrentDictionary<string, HttpClient> _clients = new ConcurrentDictionary<string, HttpClient>();

        public HttpClient CreateClient(string name)
        {
            return _clients.GetOrAdd(name, svc => new HttpClient()
            {
                // Assumes there's a k8s Service
                // REVIEW: HTTPS?, what about ports?
                BaseAddress = new Uri($"http://{svc}")
            });
        }
    }
}
