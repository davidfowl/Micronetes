using System;
using System.Collections.Generic;
using System.Text;

namespace Micronetes
{
    public class RpcClientFactory<T> : IClientFactory<T>
    {
        private readonly IClientFactory<IRpcInvoker> _clientFactory;

        public RpcClientFactory(IClientFactory<IRpcInvoker> clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public T CreateClient(string name)
        {
            // TODO: Codegen T (make sure T is an interface)
            return default;
        }
    }
}
