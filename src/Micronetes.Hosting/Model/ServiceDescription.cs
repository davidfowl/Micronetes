using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Serialization;

namespace Micronetes.Hosting.Model
{
    public class ServiceDescription
    {
        public string Name { get; set; }
        public bool External { get; set; }
        public string DockerImage { get; set; }
        public string Project { get; set; }
        public string Executable { get; set; }
        public string WorkingDirectory { get; set; }
        public string Args { get; set; }
        public int? Replicas { get; set; }
        public List<ServiceBinding> Bindings { get; set; } = new List<ServiceBinding>();
        [YamlMember(Alias = "env")]
        public Dictionary<string, string> Configuration { get; set; } = new Dictionary<string, string>();
    }
}
