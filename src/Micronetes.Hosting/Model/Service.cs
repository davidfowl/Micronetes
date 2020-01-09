using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Micronetes.Hosting.Model
{
    public class Service
    {
        public ServiceDescription Description { get; set; }

        public ConcurrentDictionary<string, object> Status { get; set; } = new ConcurrentDictionary<string, object>();

        public ConcurrentDictionary<string, ServiceReplica> Replicas { get; set; } = new ConcurrentDictionary<string, ServiceReplica>();

        [JsonIgnore]
        public Dictionary<int, List<int>> PortMap { get; set; } = new Dictionary<int, List<int>>();

        [JsonIgnore]
        public Dictionary<object, object> Items { get; } = new Dictionary<object, object>();

        [JsonIgnore]
        public List<string> Logs { get; } = new List<string>();
    }

    public class PortMapping
    {
        public int ExternalPort { get; set; }

        public List<int> InteralPorts { get; set; } = new List<int>();
    }

    public class ServiceReplica : ConcurrentDictionary<string, object>
    {
        public static JsonConverter<ServiceReplica> JsonConverter = new Converter();

        private class Converter : JsonConverter<ServiceReplica>
        {
            public override ServiceReplica Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, ServiceReplica value, JsonSerializerOptions options)
            {
                JsonSerializer.Serialize(writer, value.ToDictionary(p => p.Key, p => p.Value));
            }
        }
    }
}
