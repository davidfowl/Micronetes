using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Micronetes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Worker
{
    public class QueueWorker : BackgroundService
    {
        private readonly IClientFactory<Channel<byte[]>> _channelFactory;
        private readonly ILogger<QueueWorker> _logger;

        public QueueWorker(ILogger<QueueWorker> logger, IClientFactory<Channel<byte[]>> channelFactory)
        {
            _logger = logger;
            _channelFactory = channelFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var queue = _channelFactory.CreateClient("orders");

            await foreach (var data in queue.Reader.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation("Dequeued " + Encoding.UTF8.GetString(data));
            }
        }
    }
}
