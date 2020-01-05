using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Micronetes
{
    public class DefaultClientFactory<TClient> : IClientFactory<TClient>
    {
        private readonly ConcurrentDictionary<string, TClient> _clients = new ConcurrentDictionary<string, TClient>(StringComparer.OrdinalIgnoreCase);
        private readonly IConfiguration _configuration;

        public DefaultClientFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public TClient CreateClient(string name)
        {
            // REVIEW: Settings options configuration from where?
            var serviceAddress = _configuration[$"{name.ToUpper()}_SERVICE"];

            if (string.IsNullOrEmpty(serviceAddress))
            {
                throw new InvalidOperationException($"No such http service {name}");
            }

            var protocol = _configuration[$"{name.ToUpper()}_SERVICE_PROTOCOL"];

            // TODO: Figure out what we should do here for defaults (or is it too much magic?)

            switch (protocol?.ToLower())
            {
                default:
                    break;
            }


            throw new NotSupportedException();
        }
    }
}
