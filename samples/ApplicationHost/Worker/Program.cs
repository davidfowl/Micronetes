using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Micronetes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Worker
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            return CreateHostBuilder(args).Build().RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                       .ConfigureServices(services =>
                       {
                           services.AddMicronetes();
                           services.AddHostedService<QueueWorker>();
                       });
    }
}
