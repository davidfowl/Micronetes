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

        public int Restarts { get; set; }

        public ConcurrentDictionary<string, object> Status { get; set; } = new ConcurrentDictionary<string, object>();

        public ConcurrentDictionary<string, ReplicaStatus> Replicas { get; set; } = new ConcurrentDictionary<string, ReplicaStatus>();

        [JsonIgnore]
        public Dictionary<int, List<int>> PortMap { get; set; } = new Dictionary<int, List<int>>();

        [JsonIgnore]
        public Dictionary<object, object> Items { get; } = new Dictionary<object, object>();

        [JsonIgnore]
        public Queue<string> CachedLogs { get; } = new Queue<string>();

        [JsonIgnore]
        public Subject<string> Logs { get; set; } = new Subject<string>();
    }

    public class ProcessStatus : ReplicaStatus
    {
        public int? ExitCode { get; set; }
        public int? Pid { get; set; }
        public IDictionary<string,string> Environment { get; set; }
    }

    public class DockerStatus : ReplicaStatus
    {
        public string DockerCommand { get; set; }

        public string ContainerId { get; set; }

        public int DockerLogsPid { get; set; }
    }

    public class ReplicaStatus
    {
        public static JsonConverter<ReplicaStatus> JsonConverter = new Converter();

        public IEnumerable<int> Ports { get; set; }

        [JsonIgnore]
        public Dictionary<string, string> Metrics { get; set; } = new Dictionary<string, string>();

        private class Converter : JsonConverter<ReplicaStatus>
        {
            public override ReplicaStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, ReplicaStatus value, JsonSerializerOptions options)
            {
                // Use the runtime type since we really want to serialize either the DockerStatus or ProcessStatus
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
            }
        }
    }

    public class PortMapping
    {
        public int ExternalPort { get; set; }

        public List<int> InteralPorts { get; set; } = new List<int>();
    }
}
