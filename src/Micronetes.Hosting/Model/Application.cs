using System.Collections.Generic;
using System.Linq;

namespace Micronetes.Hosting
{
    public class Application
    {
        public Application(ServiceDescription[] services)
        {
            Services = services.ToDictionary(s => s.Name, s => new Service { Description = s });
        }

        public Dictionary<string, Service> Services { get; }
    }
}
