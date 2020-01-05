using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;

namespace Micronetes.Hosting.Model
{
    public class Service
    {
        public ServiceDescription Description { get; set; }

        public int? Pid { get; set; }

        public string State => ExitCode == null ? "Running" : "Stopped";

        [JsonIgnore]
        public Thread Thread { get; set; }

        [JsonIgnore]
        public List<string> Logs { get; } = new List<string>();

        public int? ExitCode { get; set; }
    }
}
