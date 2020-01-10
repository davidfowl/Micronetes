using System.Threading.Tasks;
using Micronetes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;

namespace BackEnd
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseMicronetes()
                .ConfigureWebHostDefaults(web =>
                {
                    web.UseStartup<Startup>()
                       .ConfigureKestrel(options =>
                       {
                           options.ConfigureEndpointDefaults(o => o.Protocols = HttpProtocols.Http2);
                       });
                });
    }
}
