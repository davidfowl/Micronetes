using System.Threading.Tasks;
using Micronetes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace FrontEnd
{
    public class FrontendApplication
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
                    web.UseStartup<FrontEnd.Startup>();
                })
                .Build()
                .RunAsync();
        }
    }
}
