using System.Collections.Generic;
using System.Linq;

namespace Micronetes.Hosting
{
    public class ServiceDescription
    {
        public string Name { get; set; }
        public bool External { get; set; }
        public List<Binding> Bindings { get; set; } = new List<Binding>();

        internal Binding DefaultBinding => Bindings.FirstOrDefault(b => b.IsDefault);
    }
}
