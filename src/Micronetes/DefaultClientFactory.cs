using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Micronetes
{
    public class DefaultClientFactory<TClient> : IClientFactory<TClient>
    {
        private readonly ConcurrentDictionary<string, TClient> _clients = new ConcurrentDictionary<string, TClient>(StringComparer.OrdinalIgnoreCase);
        private readonly INameResolver _nameResolver;

        public DefaultClientFactory(INameResolver nameResolver)
        {
            _nameResolver = nameResolver;
        }

        public TClient CreateClient(string name)
        {
            var binding = _nameResolver.GetBinding(name);

            // TODO: Figure out what we should do here for defaults (or is it too much magic?)

            switch (binding.Protocol?.ToLower())
            {
                default:
                    break;
            }


            throw new NotSupportedException();
        }
    }
}
