using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Micronetes.Hosting;
using Micronetes.Hosting.Model;

namespace ApplicationHost
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var application = new Application(new[]
            {
                new ServiceDescription {
                    Name = "frontend",
                    ProjectFile = Path.Combine("..", "FrontEnd", "FrontEnd.csproj"),
                    Bindings = new List<ServiceBinding>
                    {
                        new ServiceBinding {
                            Name = "default",
                            Address = "http://localhost:7000",
                            Protocol = "http"
                        }
                    }
                },
                new ServiceDescription {
                    Name = "backend",
                    ProjectFile = Path.Combine("..", "BackEnd", "BackEnd.csproj"),
                    Bindings = new List<ServiceBinding>
                    {
                        new ServiceBinding {
                            Name = "default",
                            Address = "http://localhost:8000",
                            Protocol = "grpc"
                        }
                    }
                },
                new ServiceDescription
                {
                    Name = "rabbit",
                    DockerImage = "rabbitmq",
                    Bindings = new List<ServiceBinding>
                    {
                        new ServiceBinding
                        {
                            Name = "default",
                            Address = "tcp://localhost:5672",
                            Protocol = "rabbitmq" // amqp?
                        }
                    }
                },
                new ServiceDescription {
                    Name = "worker",
                    ProjectFile = Path.Combine("..", "Worker", "Worker.csproj"),
                    Configuration = new Dictionary<string, string>
                    {
                        { "key", "value" }
                    }
                },
                new ServiceDescription {
                    Name = "redis",
                    DockerImage = "redis",
                    Bindings = new List<ServiceBinding>
                    {
                        new ServiceBinding {
                            Name = "default",
                            Address = "tcp://localhost:6379",
                            Protocol = "redis"
                        }
                    }
                },
            });

            await MicronetesHost.RunAsync(application, args);
        }
    }
}
