using System.Text.Json;
using System.Threading.Tasks;
using Micronetes;
using RabbitMQ.Client;
using Shared.Contracts;

namespace BackEnd.Services
{
    public class OrdersService : IOrderService
    {
        private readonly IClientFactory<IModel> _clientFactory;

        public OrdersService(IClientFactory<IModel> clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public ValueTask PlaceOrderAsync(Order order)
        {
            var channel = _clientFactory.CreateClient("Rabbit");

            channel.QueueDeclare(queue: "orders",
                                     durable: false,
                                     exclusive: false,
                                     autoDelete: false,
                                     arguments: null);

            var orderBytes = JsonSerializer.SerializeToUtf8Bytes(order);

            channel.BasicPublish(exchange: "",
                                     routingKey: "orders",
                                     basicProperties: null,
                                     body: orderBytes);

            return default;
        }
    }
}
