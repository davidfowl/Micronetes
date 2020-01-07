using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Micronetes.Hosting.Model
{
    public class Application
    {
        public Application(ServiceDescription[] services)
        {
            Services = services.ToDictionary(s => s.Name, s => new Service { Description = s });
        }

        public static Application FromYaml(string path) 
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var descriptions = deserializer.Deserialize<ServiceDescription[]>(new StringReader(File.ReadAllText(path)));

            return new Application(descriptions);
        }

        public Dictionary<string, Service> Services { get; }

        internal void PopulateEnvironment(Service service, Action<string, string> set)
        {
            // Inject normal configuration
            foreach (var pair in service.Description.Configuration)
            {
                set(pair.Key, pair.Value);
            }

            // Inject dependency information
            foreach (var s in Services.Values)
            {
                if (s == service)
                {
                    continue;
                }

                foreach (var b in s.Description.Bindings)
                {
                    string bindingName;
                    if (b.IsDefault)
                    {
                        bindingName = $"{s.Description.Name.ToUpper()}_SERVICE";
                    }
                    else
                    {
                        bindingName = $"{s.Description.Name.ToUpper()}_{b.Name.ToUpper()}_SERVICE";
                    }
                    set(bindingName, b.Address);
                    set($"{bindingName}_PROTOCOL", b.Protocol);
                }
            }
        }
    }
}
