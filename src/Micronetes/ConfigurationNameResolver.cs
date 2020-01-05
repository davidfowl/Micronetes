using System;
using Microsoft.Extensions.Configuration;

namespace Micronetes
{
    public class ConfigurationNameResolver : INameResolver
    {
        private readonly IConfiguration _configuration;

        public ConfigurationNameResolver(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public ServiceBinding GetBinding(string name)
        {
            if (!TryGetBinding(name, out var binding))
            {
                throw new InvalidOperationException($"No service {name} found. Unable to find a configuration key with the name \"{name.ToUpper()}_SERVICE\"");
            }

            return binding;
        }

        public bool TryGetBinding(string name, out ServiceBinding binding)
        {
            // TODO: Support names with /
            // - e.g {service}/{name}

            var serviceAddress = _configuration[$"{name.ToUpper()}_SERVICE"];

            if (string.IsNullOrEmpty(serviceAddress))
            {
                binding = default;
                return false;
            }

            var protocol = _configuration[$"{name.ToUpper()}_SERVICE_PROTOCOL"];

            binding = new ServiceBinding(serviceAddress, protocol);
            return true;
        }
    }
}
