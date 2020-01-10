using System.Threading.Tasks;
using Micronetes.Hosting.Model;

namespace Micronetes.Hosting
{
    public interface IExecutionTarget
    {
        Task StartAsync(Application application);

        Task StopAsync(Application application);
    }
}
