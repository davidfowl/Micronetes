using System.Threading.Tasks;
using Micronetes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace BackEnd
{
    public class BackendApplication
    {
        public Task RunAsync(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddMicronetes();
                })
                .ConfigureWebHostDefaults(web =>
                {
                    web.UseStartup<BackEnd.Startup>();
                })
                .Build()
                .RunAsync();
        }
    }
}
