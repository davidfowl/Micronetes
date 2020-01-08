using System;
using Micronetes;

namespace Microsoft.Extensions.Configuration
{
    public static class ServiceBindingConfigurationExtensions
    {
        public static ServiceBinding GetBinding(this IConfiguration configuration, string name)
        {
            var serviceAddress = configuration[$"{name.ToUpper()}_SERVICE"];

            if (string.IsNullOrEmpty(serviceAddress))
            {
                throw new InvalidOperationException($"No service {name} found. Unable to find a configuration key with the name \"{name.ToUpper()}_SERVICE\"");
            }

            var protocol = configuration[$"{name.ToUpper()}_SERVICE_PROTOCOL"];

            return new ServiceBinding(serviceAddress, protocol);
        }
    }
}
