using System;
using System.Collections.Generic;
using System.Text;

namespace Micronetes
{
    public class DefaultClientFactory<T> : IClientFactory<T>
    {
        public T CreateClient(string name)
        {
            throw new NotImplementedException();
        }
    }
}
