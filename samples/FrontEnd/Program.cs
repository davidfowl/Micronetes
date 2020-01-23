using System.Diagnostics;
using System.Threading.Tasks;
using Micronetes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using ProtoBuf.Grpc.Client;

namespace FrontEnd
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Activity.DefaultIdFormat = ActivityIdFormat.W3C;
            GrpcClientFactory.AllowUnencryptedHttp2 = true;

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(web =>
            {
                web.UseStartup<Startup>();
            });
    }
}
