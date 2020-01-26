using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Micronetes.Hosting.Model;
using Microsoft.Extensions.Logging;

namespace Micronetes.Hosting
{
    public class ReplicaStateRecorder : IApplicationProcessor
    {
        private readonly ILogger _logger;
        private readonly IDictionary<ServiceType, IReplicaInstantiator> _replicaInstantiators;
        private StateStore _store;
        
        public ReplicaStateRecorder(Application application, ILogger logger, IDictionary<ServiceType, IReplicaInstantiator> replicaInstantiators)
        {
            _logger = logger;
            _replicaInstantiators = replicaInstantiators;
            _store = new StateStore(application);
        }
        
        public async Task StartAsync(Application application)
        {
            await ProcessStaleReplicas();
            _store.Reset(true, true);
            
            foreach (var service in application.Services.Values)
            {
                if (service.Description.External)
                {
                    continue;
                }

                service.Items[typeof(Subscription)] = service.ReplicaEvents.Subscribe(OnReplicaChanged);
            }
        }

        public Task StopAsync(Application application)
        {
            foreach (var service in application.Services.Values)
            {
                if (service.Items.TryGetValue(typeof(Subscription), out var item) && item is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            
            _store.Dispose();

            return Task.CompletedTask;
        }

        private async ValueTask ProcessStaleReplicas()
        {
            var events = await _store.GetEvents();
            var tasks = new List<Task>(events.Count);
            
            foreach (var @event in events)
            {
                if (_replicaInstantiators.TryGetValue(@event.ServiceType, out var instantiator))
                {
                    var replica = await instantiator.DeserializeReplicaEvent(@event.SerializedEvent);
                    tasks.Add(instantiator.HandleStaleReplica(replica));
                }
            }

            await Task.WhenAll(tasks);
        }

        private void OnReplicaChanged(ReplicaEvent replicaEvent)
        {
            OnReplicaChangedAsync(replicaEvent).Wait(); // :(
        }

        private async Task OnReplicaChangedAsync(ReplicaEvent replicaEvent)
        {
            if (replicaEvent.State != ReplicaState.Started)
                return;
            
            var serviceType = replicaEvent.Replica.Service.ServiceType;
            if (_replicaInstantiators.TryGetValue(serviceType, out var instantiator))
            {
                var serialized = await instantiator.SerializeReplica(replicaEvent);
                await _store.WriteEvent(new StoreEvent
                {
                    ServiceType = serviceType,
                    SerializedEvent = serialized
                });
            }
        }

        private class Subscription
        {
        }

        private struct StoreEvent
        {
            public ServiceType ServiceType { get; set; }
            public string SerializedEvent { get; set; }
        }

        private class StateStore : IDisposable
        {
            //TODO: Consider Sqlite...
            
            private string _m8sFolderPath;
            private string _eventsFile;
            
            public StateStore(Application application)
            {
                _m8sFolderPath = Path.Join(Path.GetDirectoryName(application.Source), ".m8s");
                Reset(false, true);

                _eventsFile = Path.Join(_m8sFolderPath, "events");
            }

            public async Task WriteEvent(StoreEvent @event)
            {
                var contents = JsonSerializer.Serialize(@event, options: new JsonSerializerOptions
                {
                    WriteIndented = false
                });
                await File.AppendAllTextAsync(_eventsFile, contents + Environment.NewLine);
            }

            public async ValueTask<List<StoreEvent>> GetEvents()
            {
                if (!File.Exists(_eventsFile))
                {
                    return new List<StoreEvent>();
                }

                var contents = await File.ReadAllTextAsync(_eventsFile);
                var events = contents.Split(Environment.NewLine);

                return events.Where(e => !string.IsNullOrEmpty(e.Trim()))
                    .Select(e => JsonSerializer.Deserialize<StoreEvent>(e)).ToList();
            }

            public void Reset(bool delete, bool create)
            {
                if (delete && Directory.Exists(_m8sFolderPath))
                {
                    Directory.Delete(_m8sFolderPath, true);
                }

                if (create)
                {
                    Directory.CreateDirectory(_m8sFolderPath);
                }
            }

            public void Dispose()
            {
                Reset(true, false);
            }
        }
    }
}