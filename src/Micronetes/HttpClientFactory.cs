using System;
using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace Micronetes
{
    public class HttpClientFactory : IClientFactory<HttpClient>
    {
        private readonly ConcurrentDictionary<string, HttpClient> _clients = new ConcurrentDictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);
        private IConfiguration _configuration;

        public HttpClientFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public HttpClient CreateClient(string name)
        {
            var address = _configuration.GetUrl(name);

            // TODO: Check the protocol here

            return _clients.GetOrAdd(name, k => new HttpClient()
            {
                BaseAddress = address
            });
        }
    }
}
