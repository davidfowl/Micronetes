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
            var application = Application.FromYaml("app.yaml");
            await MicronetesHost.RunAsync(application, args);
        }
    }
}
