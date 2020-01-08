using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Micronetes.Hosting.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace Micronetes.Hosting
{
    public class KubernetesExecutionTarget : IExecutionTarget
    {
        private readonly ILogger _logger;
        private readonly Kubernetes _kubernetes;
        private readonly string _currentContext;

        public KubernetesExecutionTarget(ILogger logger)
        {
            _logger = logger;
            var config = KubernetesClientConfiguration.BuildDefaultConfig();
            _currentContext = config.CurrentContext;
            _kubernetes = new Kubernetes(config);

        }

        public async Task StartAsync(Application application)
        {
            _logger.LogInformation("Using k8s context: {Context}", _currentContext);

            foreach (var s in application.Services.Values)
            {
                var description = s.Description;

                // Skip external services
                if (description.External)
                {
                    continue;
                }

                if (description.Bindings.Count > 0)
                {
                    // REVIEW: Deleting before creating might be too aggressive
                    try
                    {
                        await _kubernetes.DeleteNamespacedServiceWithHttpMessagesAsync(description.Name.ToLower(), "default");
                    }
                    catch (HttpOperationException ex)
                    {
                        if (ex.Response.StatusCode != HttpStatusCode.NotFound)
                        {
                            throw;
                        }
                    }

                    // Create a service
                    var service = new k8s.Models.V1Service
                    {
                        Metadata = new k8s.Models.V1ObjectMeta
                        {
                            Name = description.Name.ToLower(),
                        },
                        Spec = new k8s.Models.V1ServiceSpec
                        {
                            Ports = BuildServicePorts(description.Bindings),
                            Selector = new Dictionary<string, string>
                            {
                                { "app", description.Name.ToLower() }
                            }
                        }
                    };

                    try
                    {
                        var response = await _kubernetes.CreateNamespacedServiceAsync(service, "default");

                        _logger.LogInformation("Created service {Name}", response.Metadata.Name);
                    }
                    catch (HttpOperationException ex)
                    {
                        if (ex.Response.StatusCode != HttpStatusCode.Conflict)
                        {
                            _logger.LogError(0, ex, ex.Response.Content);
                            throw;
                        }
                    }
                }

                // Create a deployment

                try
                {
                    var response = await _kubernetes.DeleteNamespacedDeploymentAsync(description.Name.ToLower(), "default");
                }
                catch (HttpOperationException ex)
                {
                    if (ex.Response.StatusCode != HttpStatusCode.NotFound)
                    {
                        throw;
                    }
                }


                var deployment = new k8s.Models.V1Deployment
                {
                    Metadata = new k8s.Models.V1ObjectMeta
                    {
                        Name = description.Name.ToLower(),
                    },
                    Spec = new k8s.Models.V1DeploymentSpec
                    {
                        Selector = new k8s.Models.V1LabelSelector
                        {
                            MatchLabels = new Dictionary<string, string>
                            {
                                { "app", description.Name.ToLower() }
                            }
                        },
                        Replicas = description.Replicas,
                        Template = new k8s.Models.V1PodTemplateSpec
                        {
                            Metadata = new k8s.Models.V1ObjectMeta
                            {
                                Labels = new Dictionary<string, string>
                                {
                                    { "app", description.Name.ToLower() }
                                },
                            },
                            Spec = new k8s.Models.V1PodSpec
                            {
                                Containers = new List<k8s.Models.V1Container>
                                {
                                    new k8s.Models.V1Container
                                    {
                                        Image = description.DockerImage ?? "davidfowl/featherweb", // TODO: Build image and push somewhere...
                                        Name = description.Name.ToLower(),
                                        Env = BuildEnvironment(application, s)
                                    }
                                }
                            }
                        },
                    }
                };

                try
                {
                    var response = await _kubernetes.CreateNamespacedDeploymentAsync(deployment, "default");

                    _logger.LogInformation("Created deployment {Name}", response.Metadata.Name);
                }
                catch (HttpOperationException ex)
                {
                    if (ex.Response.StatusCode != HttpStatusCode.Conflict)
                    {
                        _logger.LogError(0, ex, ex.Response.Content);
                        throw;
                    }
                }
            }
        }

        public async Task StopAsync(Application application)
        {
            foreach (var s in application.Services.Values)
            {
                var description = s.Description;

                // Skip external resources
                if (description.External)
                {
                    continue;
                }

                if (description.Bindings.Count > 0)
                {
                    try
                    {
                        var response = await _kubernetes.DeleteNamespacedServiceAsync(description.Name.ToLower(), "default");

                        _logger.LogInformation("Deleted service {Name}", description.Name.ToLower());
                    }
                    catch (HttpOperationException ex)
                    {
                        if (ex.Response.StatusCode != HttpStatusCode.NotFound)
                        {
                            _logger.LogError(0, ex, ex.Response.Content);
                            throw;
                        }
                    }
                }

                // Create a deployment

                try
                {
                    var response = await _kubernetes.DeleteNamespacedDeploymentAsync(description.Name.ToLower(), "default");

                    _logger.LogInformation("Deleted deployment {Name}", description.Name.ToLower());
                }
                catch (HttpOperationException ex)
                {
                    if (ex.Response.StatusCode != HttpStatusCode.NotFound)
                    {
                        _logger.LogError(0, ex, ex.Response.Content);
                        throw;
                    }
                }
            }
        }

        private IList<V1ServicePort> BuildServicePorts(List<ServiceBinding> bindings)
        {
            var ports = new List<k8s.Models.V1ServicePort>();

            foreach (var b in bindings)
            {
                if (b.Port != null)
                {
                    ports.Add(new k8s.Models.V1ServicePort(b.Port.Value));
                }
            }

            return ports;
        }

        private static IList<V1EnvVar> BuildEnvironment(Application application, Service service)
        {
            var env = new List<V1EnvVar>();

            if (service.Description.DockerImage == null)
            {
                var defaultBinding = service.Description.DefaultBinding;
                if (defaultBinding != null)
                {
                    env.Add(new V1EnvVar("ASPNETCORE_URLS", "https://localhost:" + defaultBinding.Port));
                }
            }

            application.PopulateEnvironment(service, (k, v) =>
            {
                env.Add(new V1EnvVar(k, v));
            });

            return env;
        }

    }
}
