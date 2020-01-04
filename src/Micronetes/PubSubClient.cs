using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Micronetes
{
    public abstract class PubSubClient
    {
        public abstract ValueTask PublishAsync(string topic, byte[] data);

        public abstract ValueTask SubscribeAsync(string topic, Func<string, byte[], Task> callback);

        public abstract ValueTask UnsubscribeAsync(string topic);
    }
}
