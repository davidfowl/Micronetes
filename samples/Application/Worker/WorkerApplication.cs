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
    public class WorkerApplication
    {
        public Task RunAsync(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                       .ConfigureServices(services =>
                       {
                           services.AddMicronetes();
                           services.AddHostedService<QueueWorker>();
                       })
                       .Build()
                       .RunAsync();
        }
    }
}
