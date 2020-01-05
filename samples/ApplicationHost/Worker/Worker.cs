using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Micronetes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Worker
{
    public class QueueWorker : IHostedService
    {
        private readonly IClientFactory<IModel> _queueFactory;
        private readonly ILogger<QueueWorker> _logger;

        public QueueWorker(ILogger<QueueWorker> logger, IClientFactory<IModel> queueFactory)
        {
            _logger = logger;
            _queueFactory = queueFactory;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var queue = _queueFactory.CreateClient("Rabbit");

            queue.QueueDeclare(queue: "orders",
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

            var consumer = new EventingBasicConsumer(queue);
            consumer.Received += (model, ea) =>
            {
                _logger.LogInformation("Dequeued " + Encoding.UTF8.GetString(ea.Body));
            };

            queue.BasicConsume(queue: "orders",
                                 autoAck: true,
                                 consumer: consumer);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
