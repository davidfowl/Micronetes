using System.Text.Json;
using System.Threading.Tasks;
using RabbitMQ.Client;
using Shared.Contracts;

namespace BackEnd.Services
{
    public class OrdersService : IOrderService
    {
        private readonly IModel _client;

        public OrdersService(IModel client)
        {
            _client = client;
        }

        public ValueTask PlaceOrderAsync(Order order)
        {
            var orderBytes = JsonSerializer.SerializeToUtf8Bytes(order);

            _client.BasicPublish(exchange: "",
                                     routingKey: "orders",
                                     basicProperties: null,
                                     body: orderBytes);

            return default;
        }
    }
}
