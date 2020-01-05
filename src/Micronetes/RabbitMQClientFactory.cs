using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Micronetes
{
    internal class RabbitMQClientFactory : IClientFactory<IModel>
    {
        private readonly ConcurrentDictionary<string, IModel> _clients = new ConcurrentDictionary<string, IModel>(StringComparer.OrdinalIgnoreCase);
        private readonly IConfiguration _configuration;

        public RabbitMQClientFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IModel CreateClient(string name)
        {
            // REVIEW: Settings options configuration from where?
            var serviceAddress = _configuration[$"{name.ToUpper()}_SERVICE"];

            if (string.IsNullOrEmpty(serviceAddress))
            {
                throw new InvalidOperationException($"No such http service {name}");
            }

            var protocol = _configuration[$"{name.ToUpper()}_SERVICE_PROTOCOL"];

            if (!string.Equals("rabbitmq", protocol, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Unsupported protocol {protocol}");
            }

            // This should be host:port
            var uri = new Uri($"amqp://{serviceAddress}");

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
