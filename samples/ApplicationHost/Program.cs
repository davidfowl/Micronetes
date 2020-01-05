using System.Collections.Generic;
using System.Threading.Tasks;
using Micronetes.Hosting;

namespace ApplicationHost
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var application = new Application(new[]
            {
                new ServiceDescription {
                    Name = "FrontEnd",
                    Bindings = new List<Binding>
                    {
                        new Binding {
                            Name = "default",
                            Address = "http://localhost:7000",
                            Protocol = "http"
                        }
                    }
                },
                new ServiceDescription {
                    Name = "BackEnd",
                    Bindings = new List<Binding>
                    {
                        new Binding {
                            Name = "default",
                            Address = "http://localhost:8000",
                            Protocol = "http"
                        }
                    }
                },
                new ServiceDescription {
                    Name = "Worker",
                },
                new ServiceDescription {
                    Name = "Redis",
                    External = true,
                    Bindings = new List<Binding>
                    {
                        new Binding {
                            Name = "default",
                            Address = "localhost:6379",
                            Protocol = "redis"
                        }
                    }
                },
                new ServiceDescription
                {
                    Name = "Rabbit",
                    External = true,
                    Bindings = new List<Binding>
                    {
                        new Binding
                        {
                            Name = "default",
                            Address = "localhost:5672",
                            Protocol = "rabbitmq" // amqp?
                        }
                    }
                }
            });

            await MicronetesHost.RunAsync(application, args);
        }
    }
}
