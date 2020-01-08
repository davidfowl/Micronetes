using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Micronetes.Hosting.Model
{
    public class Application
    {
        public string ContextDirectory { get; set; } = Directory.GetCurrentDirectory();

        public Application(ServiceDescription[] services)
        {
            Services = services.ToDictionary(s => s.Name, s => new Service { Description = s });
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
                if (d.ProjectFile == null)
                {
                    continue;
                }

                // Try to populate more from launch settings
                var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(contextDirectory, d.ProjectFile)));
                var launchSettingsPath = Path.Combine(projectDirectory, "Properties", "launchSettings.json");

                if (File.Exists(launchSettingsPath))
                {
                    // If there's a launchSettings.json, then use it to get addresses
                    var root = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(launchSettingsPath));
                    var key = Path.GetFileNameWithoutExtension(d.ProjectFile);
                    var profiles = root.GetProperty("profiles");
                    if (profiles.TryGetProperty(key, out var projectSettings))
                    {
                        // Only do this if there are no bindings
                        if (d.Bindings.Count == 0)
                        {
                            var addresses = projectSettings.GetProperty("applicationUrl").GetString()?.Split(';');

                            foreach (var address in addresses)
                            {
                                d.Bindings.Add(new ServiceBinding
                                {
                                    Name = "default",
                                    Address = address,
                                    Protocol = "http"
                                });
                            }
                        }
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
