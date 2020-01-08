using System;
using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace Micronetes
{
    public class HttpClientFactory : IClientFactory<HttpClient>
    {
        private readonly ConcurrentDictionary<string, HttpClient> _clients = new ConcurrentDictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);
        private IConfiguration _nameResolver;

        public HttpClientFactory(IConfiguration nameResolver)
        {
            _nameResolver = nameResolver;
        }

        public HttpClient CreateClient(string name)
        {
            var binding = _nameResolver.GetBinding(name);

            // TODO: Check the protocol here

            return _clients.GetOrAdd(name, k => new HttpClient()
            {
                BaseAddress = new Uri(binding.Address)
            });
        }
    }
}
