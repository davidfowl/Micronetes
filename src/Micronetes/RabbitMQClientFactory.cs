using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Micronetes
{
    internal class RabbitMQClientFactory : IClientFactory<IModel>, IDisposable
    {
        private readonly ConcurrentDictionary<string, RabbitMqClient> _clients = new ConcurrentDictionary<string, RabbitMqClient>(StringComparer.OrdinalIgnoreCase);
        private readonly IConfiguration _configuration;

        public RabbitMQClientFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IModel CreateClient(string name)
        {
            var protocol = _configuration.GetProtocol(name);

            if (!string.Equals("rabbitmq", protocol, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Unsupported protocol {protocol}");
            }

            return _clients.GetOrAdd(name, n =>
            {
                // REVIEW: What about the lifetime of these connections? Do they timeout?
                var factory = new ConnectionFactory() { HostName = _configuration.GetHost(name), Port = _configuration.GetPort(name).Value };
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
