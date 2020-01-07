using System.Threading.Tasks;
using Micronetes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace FrontEnd
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddMicronetes();
                })
                .ConfigureWebHostDefaults(web =>
                {
                    web.UseStartup<Startup>();
                });
    }
}
