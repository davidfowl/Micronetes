using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace Micronetes.Hosting
{
    public partial class MicronetesHost
    {
        private static async Task LaunchApplcationInK8s(Application application, ILogger logger)
        {
            var config = KubernetesClientConfiguration.BuildDefaultConfig();
            var klient = new Kubernetes(config);

            logger.LogInformation("Using k8s context: " + config.CurrentContext);

            foreach (var s in application.Services.Values)
            {
                var description = s.Description;

                // Skip this for now
                if (description.External)
                {
                    continue;
                }

                if (description.Bindings.Count > 0)
                {
                    try
                    {
                        await klient.DeleteNamespacedServiceWithHttpMessagesAsync(description.Name.ToLower(), "default");
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
                            Ports = new List<k8s.Models.V1ServicePort>
                            {
                                new k8s.Models.V1ServicePort(80)
                            },
                            Selector = new Dictionary<string, string>
                            {
                                { "app", description.Name.ToLower() }
                            }
                        }
                    };

                    try
                    {
                        await klient.CreateNamespacedServiceWithHttpMessagesAsync(service, "default");
                    }
                    catch (HttpOperationException ex)
                    {
                        if (ex.Response.StatusCode != HttpStatusCode.Conflict)
                        {
                            throw;
                        }
                    }
                }

                // Create a deployment

                try
                {
                    await klient.DeleteNamespacedDeploymentAsync(description.Name.ToLower(), "default");
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
                        Replicas = 2,
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
                                        Image = "davidfowl/featherweb", // TODO: Build image and push somewhere...
                                        Name = description.Name.ToLower(),
                                        Env = BuildEnvironment(application, s)
                                    }
                                }
                            }
                        },
                    }
                };

                await klient.CreateNamespacedDeploymentAsync(deployment, "default");

            }
        }

        private static IList<V1EnvVar> BuildEnvironment(Application application, Service service)
        {
            var environment = new Dictionary<string, string>();
            SetEnvironment(application, service, environment);

            var env = new List<V1EnvVar>();
            foreach (var pair in environment)
            {
                env.Add(new V1EnvVar(pair.Key, pair.Value));
            }
            return env;
        }

    }
}
