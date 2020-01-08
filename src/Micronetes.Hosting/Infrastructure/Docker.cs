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

            foreach (var env in service.Description.Configuration)
            {
                environmentArguments += $"--env {env.Key}={env.Value} ";
            }

            var binding = service.Description.DefaultBinding;
            var uri = new Uri(binding.Address);

            var command = $"run --rm -d {environmentArguments} -p {uri.Port}:{uri.Port} --name {service.Description.Name.ToLower()} {service.Description.DockerImage}";

            service.Status["dockerCommand"] = command;

            var dockerInfo = new DockerInformation();
            
            dockerInfo.Thread = new Thread(() =>
            {
                logger.LogInformation("Running docker command {Command}", command);

                var replica = service.Description.Name + "_" + Guid.NewGuid().ToString().Substring(0, 10).ToLower();
                var status = service.Replicas[replica] = new ServiceReplica();

                var result = ProcessUtil.Run("docker", command, throwOnError: false, cancellationToken: dockerInfo.StoppingTokenSource.Token);

                if (result.ExitCode != 0)
                {
                    logger.LogError("docker run failed for {ServiceName} with exit code {ExitCode}", service.Description.Name, result.ExitCode);
                    service.Replicas.Remove(replica);
                    return;
                }

                var containerId = result.StandardOutput.Trim();
                var shortContainerId = containerId.Substring(0, Math.Min(12, containerId.Length));

                status["containerId"] = shortContainerId;
                service.State = ServiceState.Running;

                logger.LogInformation("Running container {ContainerName} with ID {ContainerId}", service.Description.Name.ToLower(), shortContainerId);
                
                logger.LogInformation("Collecting docker logs for {ServiceName}.", service.Description.Name);

                ProcessUtil.Run("docker", $"logs -f {containerId}",
                    outputDataReceived: data =>
                    {
                        if (data != null)
                        {
                            service.Logs.Add(data);
                        }
                    },
                    throwOnError: false,
                    cancellationToken: dockerInfo.StoppingTokenSource.Token);

                logger.LogInformation("docker logs collection for {ServiceName} complete with exit code {ExitCode}", service.Description.Name, result.ExitCode);

                logger.LogInformation("Stopping container {ContainerName} with ID {ContainerId}", service.Description.Name.ToLower(), shortContainerId);

                ProcessUtil.Run("docker", $"stop {containerId}", throwOnError: false);
            });

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
