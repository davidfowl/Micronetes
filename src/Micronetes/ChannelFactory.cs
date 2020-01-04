using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Micronetes
{
    public class ChannelFactory : IClientFactory<Channel<byte[]>>
    {
        private static readonly ConcurrentDictionary<string, Channel<byte[]>> _queues = new ConcurrentDictionary<string, Channel<byte[]>>();

        public Channel<byte[]> CreateClient(string name)
        {
            return _queues.GetOrAdd(name, s => Channel.CreateUnbounded<byte[]>());
        }
    }
}
