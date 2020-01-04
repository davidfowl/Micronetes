using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Micronetes
{
    public class HttpServiceRegistry
    {
        public static HttpServiceRegistry Default = new HttpServiceRegistry();

        private readonly ConcurrentDictionary<string, string> _clients = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void RegisterAddress(string name, IEnumerable<string> address)
        {
            _clients.TryAdd(name, address.FirstOrDefault());
        }

        public bool TryGetValue(string name, out string address)
        {
            return _clients.TryGetValue(name, out address);
        }
    }
}
