using System.Collections.Generic;
using System.Text;

namespace Micronetes
{
    public interface INameResolver
    {
        bool TryGetBinding(string name, out ServiceBinding binding);

        ServiceBinding GetBinding(string name);
    }
}
