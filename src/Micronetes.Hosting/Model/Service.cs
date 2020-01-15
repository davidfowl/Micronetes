using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Micronetes.Hosting.Model
{
    public class Service
    {
        public Service()
        {
            Logs.Subscribe(entry =>
            {
                if (CachedLogs.Count > 5000)
                {
                    CachedLogs.Dequeue();
                }

                CachedLogs.Enqueue(entry);
            });
        }

        public ServiceDescription Description { get; set; }

        public ConcurrentDictionary<string, object> Status { get; set; } = new ConcurrentDictionary<string, object>();

        public ConcurrentDictionary<string, ServiceReplica> Replicas { get; set; } = new ConcurrentDictionary<string, ServiceReplica>();

        [JsonIgnore]
        public Dictionary<int, List<int>> PortMap { get; set; } = new Dictionary<int, List<int>>();

        [JsonIgnore]
        public Dictionary<object, object> Items { get; } = new Dictionary<object, object>();

        [JsonIgnore]
        public Queue<string> CachedLogs { get; } = new Queue<string>();

        [JsonIgnore]
        public Subject<string> Logs { get; set; } = new Subject<string>();
    }

    public class PortMapping
    {
        public int ExternalPort { get; set; }

        public List<int> InteralPorts { get; set; } = new List<int>();
    }

    public class ServiceReplica : ConcurrentDictionary<string, object>
    {
        public static JsonConverter<ServiceReplica> JsonConverter = new Converter();

        public Dictionary<string, string> Metrics { get; set; } = new Dictionary<string, string>();

        private class Converter : JsonConverter<ServiceReplica>
        {
            public override ServiceReplica Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, ServiceReplica value, JsonSerializerOptions options)
            {
                JsonSerializer.Serialize(writer, value, typeof(ConcurrentDictionary<string, object>));
            }
        }
    }
}
