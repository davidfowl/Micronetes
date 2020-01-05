using StackExchange.Redis;

namespace Micronetes
{
    public class StackExchangeRedisPubSubClientFactory : IClientFactory<PubSubClient>
    {
        private readonly IClientFactory<ConnectionMultiplexer> _clientFactory;

        public StackExchangeRedisPubSubClientFactory(IClientFactory<ConnectionMultiplexer> clientFactory)
        {
            _clientFactory = clientFactory;
        }
        public PubSubClient CreateClient(string name)
        {
            var connection = _clientFactory.CreateClient(name);
            return new StackExangeRedisPubSubClient(connection.GetSubscriber());
        }
    }
}
