using System.Collections.Generic;
using System.Linq;

namespace Micronetes.Hosting.Model
{
    public class ServiceDescription
    {
        public string Name { get; set; }
        public bool External { get; set; }
        public string DockerImage { get; set; }
        public string ProjectFile { get; set; }
        public string Executable { get; set; }
        public string WorkingDirectory { get; set; }
        public string Args { get; set; }
        public int? Replicas { get; set; }
        public List<ServiceBinding> Bindings { get; set; } = new List<ServiceBinding>();
        public Dictionary<string, string> Configuration { get; set; } = new Dictionary<string, string>();
    }
}
