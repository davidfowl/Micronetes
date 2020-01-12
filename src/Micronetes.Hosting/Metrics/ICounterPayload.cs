using System;
using System.Collections.Generic;
using System.Text;

namespace Micronetes.Hosting.Metrics
{
    public interface ICounterPayload
    {
        public string Name { get; }
        public string Value { get; }
        public string DisplayName { get; }
    }
}
