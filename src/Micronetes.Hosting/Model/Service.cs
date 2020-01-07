using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Micronetes.Hosting.Model
{
    public class Service
    {
        public ServiceDescription Description { get; set; }

        public ServiceState State { get; set; }

        public Dictionary<string, object> Status { get; set; } = new Dictionary<string, object>();

        public Dictionary<string, ServiceReplica> Replicas { get; set; } = new Dictionary<string, ServiceReplica>();

        [JsonIgnore]
        public Dictionary<object, object> Items { get; } = new Dictionary<object, object>();

        [JsonIgnore]
        public List<string> Logs { get; } = new List<string>();
    }

    public class ServiceReplica : Dictionary<string, object>
    {
        
    }
}
