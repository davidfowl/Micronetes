using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Grpc.Net.Client;

namespace Micronetes
{
    internal class GrpcChannelClientFactory : IClientFactory<GrpcChannel>
    {
        private readonly ConcurrentDictionary<string, GrpcChannel> _clients = new ConcurrentDictionary<string, GrpcChannel>(StringComparer.OrdinalIgnoreCase);
        private INameResolver _nameResolver;

        public GrpcChannelClientFactory(INameResolver nameResolver)
        {
            _nameResolver = nameResolver;
        }

        public GrpcChannel CreateClient(string name)
        {
            var binding = _nameResolver.GetBinding(name);

            // TODO: Check the protocol here

            return _clients.GetOrAdd(name, k => GrpcChannel.ForAddress(binding.Address));
        }
    }
}
