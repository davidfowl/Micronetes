using System;
using System.Collections.Concurrent;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;

namespace Micronetes
{
    public class DefaultClientFactory<TClient> : IClientFactory<TClient> where TClient : class
    {
        private readonly ConcurrentDictionary<string, TClient> _clients = new ConcurrentDictionary<string, TClient>(StringComparer.OrdinalIgnoreCase);
        private readonly INameResolver _nameResolver;

        public DefaultClientFactory(INameResolver nameResolver)
        {
            _nameResolver = nameResolver;
        }

        public TClient CreateClient(string name)
        {
            var binding = _nameResolver.GetBinding(name);

            // TODO: Figure out what we should do here for defaults (or is it too much magic?)

            switch (binding.Protocol?.ToLower())
            {
                case "grpc":
                case null:
                    return _clients.GetOrAdd(name, k => GrpcChannel.ForAddress(binding.Address).CreateGrpcService<TClient>());
                default:
                    // Default to GRPC if the TClient is a service contract
                    break;
            }


            throw new NotSupportedException($"{name} exposes protocol {binding.Protocol} which has no support for {typeof(TClient)}");
        }
    }
}
