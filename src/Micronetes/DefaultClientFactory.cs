using System;
using System.Collections.Concurrent;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using ProtoBuf.Grpc.Client;

namespace Micronetes
{
    public class DefaultClientFactory<TClient> : IClientFactory<TClient> where TClient : class
    {
        private readonly ConcurrentDictionary<string, TClient> _clients = new ConcurrentDictionary<string, TClient>(StringComparer.OrdinalIgnoreCase);
        private readonly IConfiguration _configuration;

        static DefaultClientFactory()
        {
            // A little leaky...
            GrpcClientFactory.AllowUnencryptedHttp2 = true;
        }

        public DefaultClientFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public TClient CreateClient(string name)
        {
            var address = _configuration.GetAddress(name);
            var protocol = _configuration.GetProtocol(name);

            // TODO: Figure out what we should do here for defaults (or is it too much magic?)

            switch (protocol?.ToLower())
            {
                case "grpc":
                case null:
                    return _clients.GetOrAdd(name, k => GrpcChannel.ForAddress(address).CreateGrpcService<TClient>());
                default:
                    // Default to GRPC if the TClient is a service contract
                    break;
            }


            throw new NotSupportedException($"{name} exposes protocol {address} which has no support for {typeof(TClient)}");
        }
    }
}
