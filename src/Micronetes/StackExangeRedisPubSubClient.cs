using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Micronetes
{
    public class StackExangeRedisPubSubClient : PubSubClient
    {
        private readonly ISubscriber _subscriber;

        public StackExangeRedisPubSubClient(ISubscriber subscriber)
        {
            _subscriber = subscriber;
        }
        public override async ValueTask PublishAsync(string topic, byte[] data)
        {
            await _subscriber.PublishAsync(topic, data);
        }

        public override ValueTask SubscribeAsync(string topic, Func<string, byte[], Task> callback)
        {
            var queue = _subscriber.Subscribe(topic);
            queue.OnMessage((message) => callback(message.SubscriptionChannel, message.Message));
            return default;
        }

        public override async ValueTask UnsubscribeAsync(string topic)
        {
            await _subscriber.UnsubscribeAsync(topic);
        }
    }
}
