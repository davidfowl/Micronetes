using System.Threading.Tasks;
using Micronetes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace BackEnd
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddMicronetes();
                })
                .ConfigureWebHostDefaults(web =>
                {
                    web.UseStartup<Startup>();
                })
                .Build()
                .RunAsync();
        }
    }
}
