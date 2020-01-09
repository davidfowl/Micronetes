using System;

namespace Microsoft.Extensions.Configuration
{
    public static class ConfigurationExtensions
    {
        public static string GetUrl(this IConfiguration configuration, string name, string scheme = "http")
        {
            return scheme + "://" + configuration.GetHost(name) + ":" + configuration.GetPort(name);
        }

        public static string GetHost(this IConfiguration configuration, string name)
        {
            var serviceAddress = configuration[$"service:{name}:host"];

            if (string.IsNullOrEmpty(serviceAddress))
            {
                throw new InvalidOperationException($"No service {name} found. Unable to find a configuration key with the name \"{name}:service:host\"");
            }

            return serviceAddress;
        }

        public static int GetPort(this IConfiguration configuration, string name)
        {
            var port = configuration[$"service:{name}:port"];

            if (string.IsNullOrEmpty(port))
            {
                throw new InvalidOperationException($"No service {name} found. Unable to find a configuration key with the name \"{name}:service:port\"");
            }

            return int.Parse(port);
        }


        public static string GetProtocol(this IConfiguration configuration, string name)
        {
            var protocol = configuration[$"service:{name}:protocol"];

            if (string.IsNullOrEmpty(protocol))
            {
                throw new InvalidOperationException($"No service {name} found. Unable to find a configuration key with the name \"{name}:service\"");
            }

            return protocol;
        }
    }
}
