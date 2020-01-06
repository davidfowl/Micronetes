using System;
using System.Collections.Concurrent;
using StackExchange.Redis;

namespace Micronetes
{
    internal class StackExchangeRedisClientFactory : IClientFactory<ConnectionMultiplexer>, IDisposable
    {
        private readonly ConcurrentDictionary<string, ConnectionMultiplexer> _clients = new ConcurrentDictionary<string, ConnectionMultiplexer>();

        private readonly INameResolver _nameResolver;

        public StackExchangeRedisClientFactory(INameResolver nameResolver)
        {
            _nameResolver = nameResolver;
        }

        public ConnectionMultiplexer CreateClient(string name)
        {
            // REVIEW: Settings options configuration from where?

            var binding = _nameResolver.GetBinding(name);

            if (!string.Equals("redis", binding.Protocol, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported protocol {binding.Protocol}");
            }

            return _clients.GetOrAdd(name, n =>
            {
                var uri = new Uri(binding.Address);
                // REVIEW: What about async? Do we make sure that clients all have a an explicit Connect
                return ConnectionMultiplexer.Connect(uri.Host + ":" + uri.Port);
            });
        }

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                client.Value.Dispose();
            }
        }
    }
}
