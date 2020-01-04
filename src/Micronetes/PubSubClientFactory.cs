using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Micronetes
{
    public class PubSubClientFactory : IClientFactory<PubSubClient>
    {
        private readonly ConcurrentDictionary<string, DefaultPubSubClient> _clients = new ConcurrentDictionary<string, DefaultPubSubClient>();

        public PubSubClient CreateClient(string name)
        {
            return _clients.GetOrAdd(name, n => new DefaultPubSubClient());
        }
    }
}
