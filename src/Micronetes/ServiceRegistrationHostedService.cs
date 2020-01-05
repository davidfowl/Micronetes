using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Micronetes
{
    public class ServiceRegistrationHostedService : IHostedService
    {
        private readonly ILogger<ServiceRegistrationHostedService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly HttpServiceRegistry _serviceRegistry;
        private readonly IServer _server;

        public ServiceRegistrationHostedService(ILogger<ServiceRegistrationHostedService> logger,
                                                IConfiguration configuration,
                                                IHostEnvironment environment,
                                                IHostApplicationLifetime lifetime,
                                                HttpServiceRegistry serviceRegistry,
                                                IServer server = null)
        {
            _logger = logger;
            _configuration = configuration;
            _environment = environment;
            _lifetime = lifetime;
            _serviceRegistry = serviceRegistry;
            _server = server;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var url = _configuration["API_SERVER"];

            // No API server means we're running in process, register ourself app domain wide
            _lifetime.ApplicationStarted.Register(() =>
            {
                if (string.IsNullOrEmpty(url))
                {
                    if (_server != null)
                    {
                        var serverAddresses = _server.Features.Get<IServerAddressesFeature>();
                        if (serverAddresses != null)
                        {
                            _serviceRegistry.RegisterAddress(_environment.ApplicationName, serverAddresses.Addresses);
                        }
                    }
                }
                else
                {
                    // The API server has all names and addresses
                    // This is async void... so a really bad idea but we need to do async work
                    _ = PopulateServiceRegistryAsync(url);
                }
            });

            return Task.CompletedTask;
        }

        private async Task PopulateServiceRegistryAsync(string url)
        {
            try
            {
                var client = new HttpClient()
                {
                    BaseAddress = new Uri(url)
                };

                _logger.LogInformation("Populating service registry from {url}", url);

                var response = await client.GetAsync("/api/v1/services");
                response.EnsureSuccessStatusCode();

                var services = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
                foreach (var e in services.EnumerateObject())
                {
                    var bindings = e.Value.GetProperty("Description").GetProperty("Bindings");
                    var addresses = new List<string>();

                    foreach (var binding in bindings.EnumerateArray())
                    {
                        addresses.Add(binding.GetProperty("Address").GetString());
                    }

                    _serviceRegistry.RegisterAddress(e.Name, addresses);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to populate service registry");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
