using System;
using System.Collections.Generic;
using System.Text;

namespace Micronetes
{
    public interface IClientFactory<TClient>
    {
        TClient CreateClient(string name);
    }
}
