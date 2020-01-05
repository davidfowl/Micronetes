using System.Buffers;
using System.Linq;
using System.Threading.Tasks;
using Micronetes;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;

namespace BackEnd.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class OrdersController : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> Post([FromServices]IClientFactory<IModel> channelFactory)
        {
            var channel = channelFactory.CreateClient("Rabbit");

            channel.QueueDeclare(queue: "orders",
                                     durable: false,
                                     exclusive: false,
                                     autoDelete: false,
                                     arguments: null);

            var reader = Request.BodyReader;
            while (true)
            {
                var result = await reader.ReadAsync();
                var buffer = result.Buffer;

                if (result.IsCompleted)
                {
                    channel.BasicPublish(exchange: "",
                                     routingKey: "orders",
                                     basicProperties: null,
                                     body: buffer.ToArray());

                    reader.AdvanceTo(buffer.End);
                    break;
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
            }

            return Accepted();
        }
    }
}
