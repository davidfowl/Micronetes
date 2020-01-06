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

            logger.LogInformation("Running docker command {Command}", command);

            var result = ProcessUtil.Run("docker", command);
            var containerId = result.StandardOutput.Trim();
            var shortContainerId = containerId.Substring(0, 12);

            logger.LogInformation("Running container {ContainerName} with ID {ContainerId}", service.Description.Name.ToLower(), shortContainerId);

            var dockerInfo = new DockerInformation
            {
                ContainerId = containerId,
                ShortContainerId = shortContainerId
            };

            service.Items[typeof(DockerInformation)] = dockerInfo;

            dockerInfo.LogsThread = new Thread(() =>
            {
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
                        dockerInfo.LogsPid = pid;
                    });
            });

            dockerInfo.LogsThread.Start();

            return Task.CompletedTask;
        }

        public static void Stop(ILogger logger, Service service)
        {
            if (service.Items.TryGetValue(typeof(DockerInformation), out var value) && value is DockerInformation di)
            {
                logger.LogInformation("Stopping container {ContainerName} with ID {ContainerId}", service.Description.Name.ToLower(), di.ShortContainerId);

                try
                {
                    ProcessUtil.Run("docker", $"stop {di.ContainerId}");
                }
                catch (Exception ex)
                {
                    logger.LogError(0, ex, "Failed to stop container {ContainerId}", di.ShortContainerId);
                }

                try
                {
                    ProcessUtil.StopProcess(Process.GetProcessById(di.LogsPid));
                }
                catch
                {

                }
            }
        }

        private class DockerInformation
        {
            public string ContainerId { get; set; }
            public string ShortContainerId { get; set; }
            public Thread LogsThread { get; set; }
            public int LogsPid { get; set; }
        }
    }
}
