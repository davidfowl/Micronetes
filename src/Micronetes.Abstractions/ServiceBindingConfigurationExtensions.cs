using System;

namespace Microsoft.Extensions.Configuration
{
    public static class ServiceBindingConfigurationExtensions
    {
        public static string GetAddress(this IConfiguration configuration, string name)
        {
            var serviceAddress = configuration[$"{name.ToUpper()}_SERVICE"];

            if (string.IsNullOrEmpty(serviceAddress))
            {
                throw new InvalidOperationException($"No service {name} found. Unable to find a configuration key with the name \"{name.ToUpper()}_SERVICE\"");
            }

            return serviceAddress;
        }

        public static string GetProtocol(this IConfiguration configuration, string name)
        {
            var protocol = configuration[$"{name.ToUpper()}_SERVICE_PROTOCOL"];

            if (string.IsNullOrEmpty(protocol))
            {
                throw new InvalidOperationException($"No service {name} found. Unable to find a configuration key with the name \"{name.ToUpper()}_SERVICE\"");
            }

            return protocol;
        }
    }
}
