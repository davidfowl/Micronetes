using System;
using System.Collections.Generic;
using System.Text;

namespace Micronetes.Hosting.Metrics
{
    public class IncrementingCounterPayload : ICounterPayload
    {
        private readonly string _displayName;
        private readonly string _displayRateTimeScale;
        public IncrementingCounterPayload(IDictionary<string, object> payloadFields)
        {
            Name = payloadFields["Name"].ToString();
            Value = payloadFields["Increment"].ToString();
            _displayName = payloadFields["DisplayName"].ToString();
            _displayRateTimeScale = TimeSpan.Parse(payloadFields["DisplayRateTimeScale"].ToString()).ToString("%s' sec'");
        }

        public string Name { get; }
        public string Value { get; }
        public string DisplayName { get => $"{_displayName} / {_displayRateTimeScale}"; }
    }
}
