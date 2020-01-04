using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Micronetes
{
    public interface IRpcInvoker
    {
        ValueTask InvokeAsync(string target, object[] args);

        ValueTask<T> InvokeAsync<T>(string target, object[] args);

        // TODO Streaming?
    }
}
