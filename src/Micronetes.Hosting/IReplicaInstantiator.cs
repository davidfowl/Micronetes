using System.Threading.Tasks;
using Micronetes.Hosting.Model;

namespace Micronetes.Hosting
{
    public interface IReplicaInstantiator
    {
        Task HandleStaleReplica(ReplicaEvent replicaEvent);

        ValueTask<string> SerializeReplica(ReplicaEvent replicaEvent);

        ValueTask<ReplicaEvent> DeserializeReplicaEvent(string serializedEvent);
    }
}