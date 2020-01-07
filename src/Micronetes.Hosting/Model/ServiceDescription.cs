using System.Collections.Generic;
using System.Linq;

namespace Micronetes.Hosting.Model
{
    public class ServiceDescription
    {
        public string Name { get; set; }
        public bool External { get; set; }
        public string DockerImage { get; set; }
        public Dictionary<string, string> Annotations { get; set; } = new Dictionary<string, string>();
        public List<ServiceBinding> Bindings { get; set; } = new List<ServiceBinding>();
        public Dictionary<string, string> Configuration { get; set; } = new Dictionary<string, string>();

        internal ServiceBinding DefaultBinding => Bindings.FirstOrDefault(b => b.IsDefault);
    }
}
