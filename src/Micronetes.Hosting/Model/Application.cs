using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Micronetes.Hosting.Model
{
    public class Application
    {
        public string ContextDirectory { get; set; } = Directory.GetCurrentDirectory();

        public Application(ServiceDescription[] services)
        {
            var map = new Dictionary<string, Service>();

            // TODO: Do validation here
            foreach (var s in services)
            {
                s.Replicas ??= 1;
                map[s.Name] = new Service { Description = s };
            }

            Services = map;
        }

        public static Application FromYaml(string path)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var descriptions = deserializer.Deserialize<ServiceDescription[]>(new StringReader(File.ReadAllText(path)));

            var contextDirectory = Path.GetDirectoryName(fullPath);

            foreach (var d in descriptions)
            {
                if (d.Project == null)
                {
                    continue;
                }

                // Try to populate more from launch settings
                var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(contextDirectory, d.Project)));
                var launchSettingsPath = Path.Combine(projectDirectory, "Properties", "launchSettings.json");

                if (File.Exists(launchSettingsPath))
                {
                    // If there's a launchSettings.json, then use it to get addresses
                    var root = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(launchSettingsPath));
                    var key = Path.GetFileNameWithoutExtension(d.Project);
                    var profiles = root.GetProperty("profiles");
                    if (profiles.TryGetProperty(key, out var projectSettings))
                    {
                        // Only do this if there are no bindings
                        //if (d.Bindings.Count == 0)
                        //{
                        //    var addresses = projectSettings.GetProperty("applicationUrl").GetString()?.Split(';');

                        //    foreach (var address in addresses)
                        //    {
                        //        d.Bindings.Add(new ServiceBinding
                        //        {
                        //            Name = "default",
                        //            ConnectionString = address,
                        //            Protocol = "http"
                        //        });
                        //    }
                        //}
                    }
                }
            }

            return new Application(descriptions)
            {
                // Use the file location as the context when loading from a file
                ContextDirectory = contextDirectory
            };
        }

        public static Application FromProject(string path)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));

            return new Application(new ServiceDescription[0])
            {
                ContextDirectory = Path.GetDirectoryName(fullPath)
            };
        }

        public static Application FromSolution(string path)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));

            return new Application(new ServiceDescription[0])
            {
                ContextDirectory = Path.GetDirectoryName(fullPath)
            };
        }

        public Dictionary<string, Service> Services { get; }

        internal ILoggerFactory LoggerFactory { get; set; }

        internal void PopulateEnvironment(Service service, Action<string, string> set)
        {
            if (service.Description.Configuration != null)
            {
                // Inject normal configuration
                foreach (var pair in service.Description.Configuration)
                {
                    set(pair.Key, pair.Value);
                }
            }

            void SetBinding(string serviceName, ServiceBinding b)
            {
                var configName = "";
                var envName = "";

                if (string.IsNullOrEmpty(b.Name))
                {
                    configName = serviceName;
                    envName = serviceName;
                }
                else
                {
                    configName = $"{serviceName.ToUpper()}__{b.Name.ToUpper()}";
                    envName = $"{serviceName.ToUpper()}_{b.Name.ToUpper()}";
                }

                if (!string.IsNullOrEmpty(b.ConnectionString))
                {
                    // Special case for connection strings
                    set($"CONNECTIONSTRING__{configName}", b.ConnectionString);
                }

                if (!string.IsNullOrEmpty(b.Protocol))
                {
                    // IConfiguration specific (double underscore ends up telling the configuration provider to use it as a separator)
                    set($"SERVICE__{configName}__PROTOCOL", b.Protocol);
                    set($"{envName}_SERVICE_PROTOCOL", b.Protocol);
                }

                if (b.Port != null)
                {
                    set($"SERVICE__{configName}__PORT", b.Port.ToString());
                    set($"{envName}_SERVICE_PORT", b.Port.ToString());
                }

                set($"SERVICE__{configName}__HOST", b.Host ?? "localhost");
                set($"{envName}_SERVICE_HOST", b.Host ?? "localhost");
            }

            // Inject dependency information
            foreach (var s in Services.Values)
            {
                foreach (var b in s.Description.Bindings)
                {
                    SetBinding(s.Description.Name.ToUpper(), b);
                }
            }
        }
    }
}
