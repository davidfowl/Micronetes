using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Micronetes
{
    public class DefaultPubSubClient : PubSubClient
    {
        public override ValueTask PublishAsync(string topic, byte[] data)
        {
            return default;
        }

        public override ValueTask SubscribeAsync(string topic, Func<string, byte[], Task> callback)
        {
            return default;
        }

        public override ValueTask UnsubscribeAsync(string topic)
        {
            return default;
        }
    }
}
