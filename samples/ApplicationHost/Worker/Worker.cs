using System;
using System.Runtime.ExceptionServices;
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

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                IModel queue = await ConnectAsync();

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
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, "Failed to start listening to rabbit mq");

                throw;
            }
        }
        private async Task<IModel> ConnectAsync()
        {
            ExceptionDispatchInfo edi = null;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    return _queueFactory.CreateClient("Rabbit");
                }
                catch (Exception ex)
                {
                    if (i == 4)
                    {
                        edi = ExceptionDispatchInfo.Capture(ex);
                    }

                    _logger.LogError(0, ex, "Failed to start listening to rabbit mq");
                }

                await Task.Delay(5000);
            }

            edi.Throw();
            return null;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
