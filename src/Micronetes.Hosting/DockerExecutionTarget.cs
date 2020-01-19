using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Micronetes.Hosting.Model;
using Microsoft.Extensions.Logging;

namespace Micronetes.Hosting
{
    public class DockerExecutionTarget : IExecutionTarget
    {
        private readonly ILogger _logger;

        public DockerExecutionTarget(ILogger logger)
        {
            _logger = logger;
        }

        public Task StartAsync(Application application)
        {
            var tasks = new Task[application.Services.Count];
            var index = 0;
            foreach (var s in application.Services)
            {
                tasks[index++] = s.Value.Description.External ? Task.CompletedTask : StartContainerAsync(application, s.Value);
            }

            return Task.WhenAll(tasks);
        }

        public Task StopAsync(Application application)
        {
            var services = application.Services;

            var index = 0;
            var tasks = new Task[services.Count];
            foreach (var s in services.Values)
            {
                var state = s;
                tasks[index++] = Task.Run(() => StopContainer(state));
            }

            return Task.WhenAll(tasks);
        }

        private Task StartContainerAsync(Application application, Service service)
        {
            if (service.Description.DockerImage == null)
            {
                return Task.CompletedTask;
            }

            var serviceDescription = service.Description;
            var environmentArguments = "";

            var dockerInfo = new DockerInformation()
            {
                Threads = new Thread[service.Description.Replicas.Value]
            };

            void RunDockerContainer(IEnumerable<(int Port, int BindingPort, string Protocol)> ports)
            {
                var hasPorts = ports.Any();

                var replica = service.Description.Name.ToLower() + "_" + Guid.NewGuid().ToString().Substring(0, 10).ToLower();
                var status = new DockerStatus();
                service.Replicas[replica] = status;

                var environment = new Dictionary<string, string>
                {
                    // Default to development environment
                    ["DOTNET_ENVIRONMENT"] = "Development",
                    // Remove the color codes from the console output
                    ["DOTNET_LOGGING__CONSOLE__DISABLECOLORS"] = "true"
                };

                var portString = "";

                if (hasPorts)
                {
                    status.Ports = ports.Select(p => p.Port);

                    portString = string.Join(" ", ports.Select(p => $"-p {p.Port}:{p.Port}"));

                    // These ports should also be passed in not assuming ASP.NET Core
                    environment["ASPNETCORE_URLS"] = string.Join(";", ports.Select(p => $"{p.Protocol ?? "http"}://*:{p.Port}"));

                    foreach (var p in ports)
                    {
                        environment[$"{p.Protocol?.ToUpper() ?? "HTTP"}_PORT"] = p.BindingPort.ToString();
                    }
                }

                application.PopulateEnvironment(service, (key, value) => environment[key] = value, "host.docker.internal");

                environment["APP_INSTANCE"] = replica;

                foreach (var pair in environment)
                {
                    environmentArguments += $"-e {pair.Key}={pair.Value} ";
                }

                var command = $"run -d {environmentArguments} {portString} --name {replica} --restart=unless-stopped {service.Description.DockerImage}";
                _logger.LogInformation("Running docker command {Command}", command);

                status.DockerCommand = command;

                var result = ProcessUtil.Run("docker", command, throwOnError: false, cancellationToken: dockerInfo.StoppingTokenSource.Token);

                if (result.ExitCode != 0)
                {
                    _logger.LogError("docker run failed for {ServiceName} with exit code {ExitCode}:" + result.StandardError, service.Description.Name, result.ExitCode);
                    service.Replicas.TryRemove(replica, out _);

                    service.Logs.OnNext("[" + replica + "]: " + result.StandardError);
                    return;
                }

                var containerId = result.StandardOutput.Trim();

                // There's a race condition that sometimes makes us miss the output
                // so keep trying to get the container id
                while (string.IsNullOrEmpty(containerId))
                {
                    // Try to get the ID of the container
                    result = ProcessUtil.Run("docker", $"ps --no-trunc -f name={replica} --format " + "{{.ID}}");

                    containerId = result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
                }

                var shortContainerId = containerId.Substring(0, Math.Min(12, containerId.Length));

                status.ContainerId = shortContainerId;

                _logger.LogInformation("Running container {ContainerName} with ID {ContainerId}", replica, shortContainerId);

                _logger.LogInformation("Collecting docker logs for {ContainerName}.", replica);

                ProcessUtil.Run("docker", $"logs -f {containerId}",
                    outputDataReceived: data => service.Logs.OnNext("[" + replica + "]: " + data),
                    onStart: pid =>
                    {
                        status.DockerLogsPid = pid;
                    },
                    throwOnError: false,
                    cancellationToken: dockerInfo.StoppingTokenSource.Token);

                _logger.LogInformation("docker logs collection for {ContainerName} complete with exit code {ExitCode}", replica, result.ExitCode);

                // Docker has a tendency to hang so we're going to timeout this shutdown process
                var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                _logger.LogInformation("Stopping container {ContainerName} with ID {ContainerId}", replica, shortContainerId);

                result = ProcessUtil.Run("docker", $"stop {containerId}", throwOnError: false, cancellationToken: timeoutCts.Token);

                if (result.ExitCode != 0)
                {
                    service.Logs.OnNext("[" + replica + "]: " + result.StandardOutput);
                    service.Logs.OnNext("[" + replica + "]: " + result.StandardError);
                }

                _logger.LogInformation("Stopped container {ContainerName} with ID {ContainerId} exited with {ExitCode}", replica, shortContainerId, result.ExitCode);

                result = ProcessUtil.Run("docker", $"rm {containerId}", throwOnError: false, cancellationToken: timeoutCts.Token);

                if (result.ExitCode != 0)
                {
                    service.Logs.OnNext("[" + replica + "]: " + result.StandardOutput);
                    service.Logs.OnNext("[" + replica + "]: " + result.StandardError);
                }

                _logger.LogInformation("Removed container {ContainerName} with ID {ContainerId} exited with {ExitCode}", replica, shortContainerId, result.ExitCode);

                service.Replicas.TryRemove(replica, out _);
            };

            if (serviceDescription.Bindings.Count > 0)
            {
                // Each replica is assigned a list of internal ports, one mapped to each external
                // port
                for (int i = 0; i < serviceDescription.Replicas; i++)
                {
                    var ports = new List<(int, int, string)>();
                    foreach (var binding in serviceDescription.Bindings)
                    {
                        if (binding.Port == null)
                        {
                            continue;
                        }

                        ports.Add((service.PortMap[binding.Port.Value][i], binding.Port.Value, binding.Protocol));
                    }

                    dockerInfo.Threads[i] = new Thread(() => RunDockerContainer(ports));
                }
            }
            else
            {
                for (int i = 0; i < service.Description.Replicas; i++)
                {
                    dockerInfo.Threads[i] = new Thread(() => RunDockerContainer(null));
                }
            }

            for (int i = 0; i < service.Description.Replicas; i++)
            {
                dockerInfo.Threads[i].Start();
            }

            service.Items[typeof(DockerInformation)] = dockerInfo;

            return Task.CompletedTask;
        }

        private void StopContainer(Service service)
        {
            if (service.Items.TryGetValue(typeof(DockerInformation), out var value) && value is DockerInformation di)
            {
                di.StoppingTokenSource.Cancel();

                foreach (var t in di.Threads)
                {
                    t.Join();
                }
            }
        }

        private class DockerInformation
        {
            public Thread[] Threads { get; set; }
            public CancellationTokenSource StoppingTokenSource { get; set; } = new CancellationTokenSource();
        }
    }
}
