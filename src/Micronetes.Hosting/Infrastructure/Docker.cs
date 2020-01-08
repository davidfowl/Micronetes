using System;
using System.Diagnostics;
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

            var environmentArguments = "";

            if (service.Description.Configuration != null)
            {
                foreach (var env in service.Description.Configuration)
                {
                    environmentArguments += $"--env {env.Key}={env.Value} ";
                }
            }

            var binding = service.Description.DefaultBinding;

            var dockerInfo = new DockerInformation();

            void RunDockerContainer()
            {
                var replica = service.Description.Name.ToLower() + "_" + Guid.NewGuid().ToString().Substring(0, 10).ToLower();
                var status = service.Replicas[replica] = new ServiceReplica();

                var command = $"run --rm -d {environmentArguments} -p {binding.Port}:{binding.Port} --name {replica} {service.Description.DockerImage}";
                logger.LogInformation("Running docker command {Command}", command);

                status["dockerCommand"] = command;

                var result = ProcessUtil.Run("docker", command, throwOnError: false, cancellationToken: dockerInfo.StoppingTokenSource.Token);

                if (result.ExitCode != 0)
                {
                    logger.LogError("docker run failed for {ServiceName} with exit code {ExitCode}", service.Description.Name, result.ExitCode);
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

                status["containerId"] = shortContainerId;
                service.State = ServiceState.Running;

                logger.LogInformation("Running container {ContainerName} with ID {ContainerId}", replica, shortContainerId);

                logger.LogInformation("Collecting docker logs for {ContainerName}.", replica);

                ProcessUtil.Run("docker", $"logs -f {containerId}",
                    outputDataReceived: data =>
                    {
                        if (data != null)
                        {
                            service.Logs.Add(data);
                        }
                    },
                    onStart: pid =>
                    {
                        status["logsPid"] = pid;
                    },
                    throwOnError: false,
                    cancellationToken: dockerInfo.StoppingTokenSource.Token);

                logger.LogInformation("docker logs collection for {ContainerName} complete with exit code {ExitCode}", replica, result.ExitCode);

                logger.LogInformation("Stopping container {ContainerName} with ID {ContainerId}", replica, shortContainerId);

                result = ProcessUtil.Run("docker", $"stop {containerId}", throwOnError: false);

                logger.LogInformation("Stopped container {ContainerName} with ID {ContainerId} exited with {ExitCode}", replica, shortContainerId, result.ExitCode);
            };

            dockerInfo.Thread = new Thread(RunDockerContainer);
            dockerInfo.Thread.Start();

            service.Items[typeof(DockerInformation)] = dockerInfo;

            return Task.CompletedTask;
        }

        public static void Stop(Service service)
        {
            if (service.Items.TryGetValue(typeof(DockerInformation), out var value) && value is DockerInformation di)
            {
                di.StoppingTokenSource.Cancel();

                di.Thread.Join();
            }
        }

        private class DockerInformation
        {
            public Thread Thread { get; set; }
            public CancellationTokenSource StoppingTokenSource { get; set; } = new CancellationTokenSource();
        }
    }
}
