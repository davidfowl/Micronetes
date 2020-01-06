using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Micronetes.Hosting.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Micronetes.Hosting
{
    public class InProcessExecutionTarget : IExecutionTarget
    {
        private readonly ILogger _logger;

        public InProcessExecutionTarget(ILogger logger)
        {
            _logger = logger;
        }

        public Task StartAsync(Application application)
        {
            var tasks = new Task[application.Services.Count];
            var index = 0;
            foreach (var s in application.Services)
            {
                tasks[index++] = s.Value.Description.External ? Task.CompletedTask : LaunchService(application, s.Value);
            }

            return Task.WhenAll(tasks);
        }

        private Task LaunchService(Application application, Service service)
        {
            var serviceDescription = service.Description;
            var serviceName = serviceDescription.Name;
            var path = GetDllPath(serviceName);
            var contentRoot = Path.Combine(Directory.GetCurrentDirectory(), serviceName);

            // TODO: Run in different load context.
            var assembly = Assembly.LoadFrom(path);

            // In theory, this could be done with an IHostingStartup but it doesn't work with
            // the generic host as yet.
            var createHostBuilderMethod = assembly.EntryPoint?.DeclaringType.GetMethod("CreateHostBuilder", BindingFlags.Public | BindingFlags.Static);

            if (createHostBuilderMethod == null)
            {
                _logger.LogError("{Type} must have a CreateHostBuilder method.", assembly.EntryPoint?.DeclaringType);
                return Task.CompletedTask;
            }

            _logger.LogInformation("Running {assembly} in process", assembly);

            var hostBuilder = (IHostBuilder)createHostBuilderMethod.Invoke(null, new object[] { Array.Empty<string>() });

            // Setup the content root
            hostBuilder.UseContentRoot(contentRoot);

            hostBuilder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new ServiceLoggerProvider(service));
            });

            // Set the environment variables
            hostBuilder.ConfigureAppConfiguration(b =>
            {
                // Populate dependency information
                var configuration = new Dictionary<string, string>();
                application.PopulateEnvironment(service, (k, v) => configuration[k] = v);

                var defaultBinding = service.Description.DefaultBinding;

                if (defaultBinding != null)
                {
                    configuration[WebHostDefaults.ServerUrlsKey] = defaultBinding.Address;
                }

                b.AddInMemoryCollection(configuration);
            });

            hostBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<IHostLifetime, NoopLifetime>();
            });

            var host = hostBuilder.Build();

            service.Items[typeof(IHost)] = host;

            return host.StartAsync();
        }


        public async Task StopAsync(Application application)
        {
            var tasks = new Task[application.Services.Count];
            var index = 0;
            var hosts = new List<IHost>();

            foreach (var service in application.Services.Values)
            {
                if (service.Items.TryGetValue(typeof(IHost), out var hostObj) && hostObj is IHost host)
                {
                    hosts.Add(host);
                    tasks[index++] = host.StopAsync();
                }
                else
                {
                    tasks[index++] = Task.CompletedTask;
                }
            }

            await Task.WhenAll(tasks);

            hosts.ForEach(h => h.Dispose());
        }

        private static string GetDllPath(string serviceName)
        {
            // TODO: How do we determine the output path? Assembly attribute compiled in by the build system?
            return Path.Combine(Directory.GetCurrentDirectory(), serviceName, "bin", "Debug", "netcoreapp3.1", serviceName + ".dll");
        }

        private class NoopLifetime : IHostLifetime
        {
            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task WaitForStartAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        private class ServiceLoggerProvider : ILoggerProvider
        {
            private readonly Service _service;

            public ServiceLoggerProvider(Service service)
            {
                _service = service;
            }

            public ILogger CreateLogger(string categoryName)
            {
                return new Logger(_service, categoryName);
            }

            public void Dispose()
            {
                
            }

            private class Logger : ILogger
            {
                private readonly Service _service;
                private readonly string _categoryName;

                public Logger(Service service, string categoryName)
                {
                    _service = service;
                    _categoryName = categoryName;
                }

                public IDisposable BeginScope<TState>(TState state)
                {
                    return null;
                }

                public bool IsEnabled(LogLevel logLevel)
                {
                    return true;
                }

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                {
                    lock (_service.Logs)
                    {
                        if (exception != null)
                        {
                            _service.Logs.Add($"[{logLevel}]: " + exception.ToString());
                        }
                        else
                        {
                            _service.Logs.Add($"[{logLevel}]: " + formatter(state, exception));
                        }
                    }
                }
            }
        }
    }
}
