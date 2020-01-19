using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Micronetes.Hosting.Model;
using Microsoft.Extensions.Logging;

namespace Micronetes.Hosting.Infrastructure
{
    internal class Docker
    {
        public static Task RunAsync(ILogger logger, Service service)
        {
            if (service.Description.DockerImage == null)
            {
                return Task.CompletedTask;
            }

            var serviceDescription = service.Description;
            var environmentArguments = "";

            if (serviceDescription.Configuration != null)
            {
                foreach (var env in serviceDescription.Configuration)
                {
                    environmentArguments += $"--env {env.Name}={env.Value} ";
                }
            }

            var dockerInfo = new DockerInformation()
            {
                Threads = new Thread[service.Description.Replicas.Value]
            };

            void RunDockerContainer(Dictionary<int, int> ports)
            {
                var replica = service.Description.Name.ToLower() + "_" + Guid.NewGuid().ToString().Substring(0, 10).ToLower();
                var status = new DockerStatus();
                service.Replicas[replica] = status;

                var hasPorts = ports?.Any() ?? false;
                var portString = hasPorts ? string.Join(" ", ports.Select(p => $"-p {p.Value}:{p.Key}")) : "";

                var command = $"run -d {environmentArguments} {portString} --name {replica} --restart=unless-stopped {service.Description.DockerImage}";
                logger.LogInformation("Running docker command {Command}", command);

                status.DockerCommand = command;

                if (hasPorts)
                {
                    status.Ports = ports.Values;
                }

                var result = ProcessUtil.Run("docker", command, throwOnError: false, cancellationToken: dockerInfo.StoppingTokenSource.Token);

                if (result.ExitCode != 0)
                {
                    logger.LogError("docker run failed for {ServiceName} with exit code {ExitCode}:" + result.StandardError, service.Description.Name, result.ExitCode);
                    service.Replicas.TryRemove(replica, out _);
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

                logger.LogInformation("Running container {ContainerName} with ID {ContainerId}", replica, shortContainerId);

                logger.LogInformation("Collecting docker logs for {ContainerName}.", replica);

                ProcessUtil.Run("docker", $"logs -f {containerId}",
                    outputDataReceived: service.Logs.OnNext,
                    onStart: pid =>
                    {
                        status.DockerLogsPid = pid;
                    },
                    throwOnError: false,
                    cancellationToken: dockerInfo.StoppingTokenSource.Token);

                logger.LogInformation("docker logs collection for {ContainerName} complete with exit code {ExitCode}", replica, result.ExitCode);

                // Docker has a tendency to hang so we're going to timeout this shutdown process
                var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                logger.LogInformation("Stopping container {ContainerName} with ID {ContainerId}", replica, shortContainerId);

                result = ProcessUtil.Run("docker", $"stop {containerId}", throwOnError: false, cancellationToken: timeoutCts.Token);

                if (result.ExitCode != 0)
                {
                    service.Logs.OnNext(result.StandardOutput);
                    service.Logs.OnNext(result.StandardError);
                }

                logger.LogInformation("Stopped container {ContainerName} with ID {ContainerId} exited with {ExitCode}", replica, shortContainerId, result.ExitCode);

                result = ProcessUtil.Run("docker", $"rm {containerId}", throwOnError: false, cancellationToken: timeoutCts.Token);

                if (result.ExitCode != 0)
                {
                    service.Logs.OnNext(result.StandardOutput);
                    service.Logs.OnNext(result.StandardError);
                }

                logger.LogInformation("Removed container {ContainerName} with ID {ContainerId} exited with {ExitCode}", replica, shortContainerId, result.ExitCode);
            };

            if (serviceDescription.Bindings.Count > 0)
            {
                // Each replica is assigned a list of internal ports, one mapped to each external
                // port
                for (int i = 0; i < serviceDescription.Replicas; i++)
                {
                    var ports = new Dictionary<int, int>();
                    foreach (var binding in serviceDescription.Bindings)
                    {
                        if (binding.Port == null)
                        {
                            continue;
                        }

                        ports[binding.Port.Value] = service.PortMap[binding.Port.Value][i];
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

        public static void Stop(Service service)
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
