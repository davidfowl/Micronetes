using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Micronetes
{
    internal class StackExchangeRedisClientFactory : IClientFactory<ConnectionMultiplexer>
    {
        private readonly ConcurrentDictionary<string, ConnectionMultiplexer> _clients = new ConcurrentDictionary<string, ConnectionMultiplexer>();

        private readonly IConfiguration _configuration;

        public StackExchangeRedisClientFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public ConnectionMultiplexer CreateClient(string name)
        {
            // REVIEW: Settings options configuration from where?

            var serviceAddress = _configuration[$"{name.ToUpper()}_SERVICE"];

            if (string.IsNullOrEmpty(serviceAddress))
            {
                throw new InvalidOperationException($"No such http service {name}");
            }

            var protocol = _configuration[$"{name.ToUpper()}_SERVICE_PROTOCOL"];

            if (!string.Equals("redis", protocol, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported protocol {protocol}");
            }

            return _clients.GetOrAdd(name, n =>
            {
                // REVIEW: What about async? Do we make sure that clients all have a an explicit Connect
                return ConnectionMultiplexer.Connect(serviceAddress);
            });
        }
    }
}
