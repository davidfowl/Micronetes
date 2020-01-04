using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Micronetes
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // REVIEW: This could also launch 3 processes
            var tasks = new[]
            {
                FrontEnd.Program.Main(DefineService(args, "FrontEnd")),
                BackEnd.Program.Main(DefineService(args, "BackEnd")),
                Worker.Program.Main(DefineService(args, "Worker"))
            };

            Task.WaitAll(tasks);
        }

        private static string[] DefineService(string[] args, string serviceName)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), serviceName);

            return CombineArgs(args, $"--urls=http://127.0.0.1:0", $"--contentRoot={path}");
        }

        private static string[] CombineArgs(string[] args, params string[] newArgs)
        {
            return args.Concat(newArgs).ToArray();
        }
    }
}
