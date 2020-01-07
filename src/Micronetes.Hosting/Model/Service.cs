using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;

namespace Micronetes.Hosting.Model
{
    public class Service
    {
        public ServiceDescription Description { get; set; }

        public Dictionary<string, object> Status { get; set; } = new Dictionary<string, object>();

        [JsonIgnore]
        public Dictionary<object, object> Items { get; } = new Dictionary<object, object>();

        [JsonIgnore]
        public List<string> Logs { get; } = new List<string>();
    }
}
