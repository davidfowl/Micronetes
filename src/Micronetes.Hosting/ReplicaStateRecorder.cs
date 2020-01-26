using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Micronetes.Hosting.Model;
using Microsoft.Extensions.Logging;

namespace Micronetes.Hosting
{
    public class ReplicaStateRecorder : IApplicationProcessor
    {
        private readonly ILogger _logger;
        private readonly IDictionary<ServiceType, IReplicaInstantiator> _replicaInstantiators;
        
        public ReplicaStateRecorder(ILogger logger, IDictionary<ServiceType, IReplicaInstantiator> replicaInstantiators)
        {
            _logger = logger;
            _replicaInstantiators = replicaInstantiators;
        }
        
        public Task StartAsync(Application application)
        {
            //TODO: Fetch from store and process events
            
            foreach (var service in application.Services.Values)
            {
                if (service.Description.External)
                {
                    continue;
                }

                service.Items[typeof(Subscription)] = service.ReplicaEvents.Subscribe(OnReplicaChanged);
            }

            return Task.CompletedTask;
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
            
            //TODO: Delete events store

            return Task.CompletedTask;
        }

        private void OnReplicaChanged(ReplicaEvent replicaEvent)
        {
            var serviceType = replicaEvent.Replica.Service.ServiceType;
            if (_replicaInstantiators.TryGetValue(serviceType, out var instantiator))
            {
                var serialized = instantiator.SerializeReplica(replicaEvent);
            }
        }

        private class Subscription
        {
        }
    }
}