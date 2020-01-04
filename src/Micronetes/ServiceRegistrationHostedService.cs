using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Micronetes
{
    public class ServiceRegistrationHostedService : IHostedService
    {
        public ServiceRegistrationHostedService(IConfiguration configuration,
                                                IHostEnvironment environment,
                                                IHostApplicationLifetime lifetime, 
                                                HttpServiceRegistry serviceRegistry, 
                                                IServer server = null)
        {
            lifetime.ApplicationStarted.Register(() =>
            {
                if (server != null)
                {
                    var serverAddresses = server.Features.Get<IServerAddressesFeature>();
                    if (serverAddresses != null)
                    {
                        serviceRegistry.RegisterAddress(environment.ApplicationName, serverAddresses.Addresses.FirstOrDefault());
                    }
                }
            });
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
