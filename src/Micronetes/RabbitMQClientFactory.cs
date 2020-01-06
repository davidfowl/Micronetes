using System;
using System.Collections.Concurrent;
using RabbitMQ.Client;

namespace Micronetes
{
    internal class RabbitMQClientFactory : IClientFactory<IModel>, IDisposable
    {
        private readonly ConcurrentDictionary<string, RabbitMqClient> _clients = new ConcurrentDictionary<string, RabbitMqClient>(StringComparer.OrdinalIgnoreCase);
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
            var uri = new Uri(binding.Address);

            return _clients.GetOrAdd(name, n =>
            {
                // REVIEW: What about the lifetime of these connections? Do they timeout?
                var factory = new ConnectionFactory() { HostName = uri.Host, Port = uri.Port };
                var connection = factory.CreateConnection();
                var model = connection.CreateModel();

                var mq = new RabbitMqClient(connection, model);
                return mq;
            }).Model;
        }

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                client.Value.Dispose();
            }
        }

        private class RabbitMqClient : IDisposable
        {
            private readonly IConnection _connection;

            public RabbitMqClient(IConnection connection, IModel model)
            {
                _connection = connection;
                Model = model;
            }

            public IModel Model { get; }

            public void Dispose()
            {
                Model.Dispose();
                _connection.Dispose();
            }
        }
    }
}
