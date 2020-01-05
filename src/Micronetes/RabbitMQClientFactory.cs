using System;
using System.Collections.Concurrent;
using RabbitMQ.Client;

namespace Micronetes
{
    internal class RabbitMQClientFactory : IClientFactory<IModel>
    {
        private readonly ConcurrentDictionary<string, IModel> _clients = new ConcurrentDictionary<string, IModel>(StringComparer.OrdinalIgnoreCase);
        private readonly INameResolver _nameResolver;

        public RabbitMQClientFactory(INameResolver nameResolver)
        {
            _nameResolver = nameResolver;
        }

        public IModel CreateClient(string name)
        {
            var binding = _nameResolver.GetBinding(name);

            if (!string.Equals("rabbitmq", binding.Protocol, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Unsupported protocol {binding.Protocol}");
            }

            // This should be host:port
            var uri = new Uri($"amqp://{binding.Address}");

            return _clients.GetOrAdd(name, n =>
            {
                // REVIEW: What about the lifetime of these connections? Do they timeout?

                var factory = new ConnectionFactory() { HostName = uri.Host, Port = uri.Port };
                var connection = factory.CreateConnection();
                return connection.CreateModel();
            });
        }
    }
}
