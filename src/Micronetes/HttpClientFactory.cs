using System;
using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace Micronetes
{
    public class HttpClientFactory : IClientFactory<HttpClient>
    {
        private readonly ConcurrentDictionary<string, HttpClient> _clients = new ConcurrentDictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);
        private readonly IConfiguration _configuration;

        public HttpClientFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public HttpClient CreateClient(string name)
        {
            // REVIEW: Settings options configuration from where?

            var serviceAddress = _configuration[$"{name.ToUpper()}_SERVICE"];

            if (string.IsNullOrEmpty(serviceAddress))
            {
                throw new InvalidOperationException($"No such http service {name}");
            }


            return _clients.GetOrAdd(name, k => new HttpClient()
            {
                BaseAddress = new Uri(serviceAddress)
            });
        }
    }
}
