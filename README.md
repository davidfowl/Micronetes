# Micronetes

[![feedz.io](https://img.shields.io/badge/endpoint.svg?url=https%3A%2F%2Ff.feedz.io%2Fdavidfowl%2Fmicronetes%2Fshield%2Fm8s%2Flatest)](https://f.feedz.io/davidfowl/micronetes/packages/m8s/latest/download)

Micronetes is a local orchestrator inspired by kubernetes that makes developing and testing microservices and distributed applications easier.

## Why not minikube, k3s, k5s, microk8s etc?

The things that lead me to this solution:
- Developers mostly care about the projects and code they write.
- It's very hard today to run multiple applications (replicas or different applications) locally and 
set them up so that they can talk.
    - It's even hard to experiment with different application architectures that require multiple projects/applications.
- Keeping a docker file in sync with the project/solution is painful.
- Building a docker image on every change is too slow for the developer inner loop.
- When developing, you only need to run a small number of applications and dependencies.
- This should be an on-ramp to running in something like kubernetes because the basic primitives should be very similar (service, replicas, ingress (TBD)).

## So what is it?

This project is broken into 2 loosely coupled components:
- **The Micronetes CLI** - This is the orchestrator used for development and testing.
- **The Micronetes SDK** - This adds a layer on top of the naming conventions introduced by the orchestrator.

## Micronetes CLI

The Micronetes CLI is an orchestrator that coordinates multiple applications running both locally and remotely to make developing easier. Micronetes natively understands .NET Core projects so by default, no extra manifests files are needed. The CLI will use the project's launch settings to know how to run. More advanced scenarios require a manifest that instructs the micronetes CLI what projects to launch.

```
/api
/worker
/library
app.sln
```

**m8s run** will run *all* projects that have a `Properties\launchSettings.json`.

The core model is an application which is made up of several services. Here's an example of a small distributed application:

**m8s.yaml**

```yaml
- name: web
  project: Web\Web.csproj
  replicas: 2
  bindings:
    - port: 5005
- name: worker
  project: Worker\Worker.csproj
  replicas: 3
- name: rabbitmq
  dockerImage: rabbitmq
  bindings:
    - port: 5672
```

**m8s run** will run launch 2 instances of the Web and 3 of Worker and will `docker run` the rabbitmq image. There's a built-in proxy that will load balance the traffic in a round-robin matter between replicas of the various processes. It will also make those services available via environment variables following the convention described later in this section.

There is also a mini control plane built in that can be used to view the state of the services and to view the logs of the various services. As an example:

```
> m8s run
[03:26:23 INF] Mapping external port 5005 to internal port(s) 51016, 51017 for web
[03:26:23 INF] API server running on http://127.0.0.1:51018
[03:26:23 INF] Launching service web_65758656-1 from /apps/Web/bin/Debug/netcoreapp3.1/Web
[03:26:23 INF] Launching service web_65758456-d from /apps/Web/bin/Debug/netcoreapp3.1/Web
[03:26:23 INF] Launching service worker_e4cd0888-7 from /apps/Worker/bin/Debug/netcoreapp3.1/Worker
[03:26:23 INF] worker_65758656-1 running on process id 8472
[03:26:23 INF] web_e4cd0888-7 running on process id 21932 bound to http://localhost:51016
[03:26:23 INF] web_65758456-d running on process id 21932 bound to http://localhost:51017
...
```

You can run the following commands
- `curl http://127.0.0.1:51018/api/v1/services` - This will show the state of all services
- `curl http://127.0.0.1:51018/api/v1/web` - This will show the state of the web service
- `curl http://127.0.0.1:51018/api/v1/logs/worker` - This will show the logs for worker

**Reference**

```yaml
- name: string  # name of the service
  dockerImage: string  # a docker image to run locally
  project: string  # msbuild project path (relative to this file)
  executable: string # path to an executable (relative to this file)
  workingDirectory: string # working directory of the process (relative to this file)
  args: string # arguments to pass to the process
  replicas: number # number of times to launch the application
  external: bool # This service is external to avoid provisioning
  env: # environment variables
  bindings: # array of bindings (ports, connection strings etc)
    name: string # name of the binding
    port: number # port of the binding
    host: string # host of the binding
    connectionString: # connection string of the binding
```

### Service Descriptions

A service description is yaml file with list of services. Services can have multiple bindings that describe how the application can connect to it.

```yaml
- name: redis
  dockerImage: redis:5
  bindings:
    - port: 6379
      protocol: redis
```

Examples:

**Executable listening on port 80**

```yaml
- name: dapr
  executable: daprd
  bindings:
    - port: 80
      protocol: dapr
```

**HTTP(s)**

```yaml
- name: myweb
  project: MyWeb/Web.csproj
  bindings:
    - port: 80
    - name: management
      port: 3000
```

These service names are injected into the application as environment variables by the orchestrator. This allows the client code to access the address information at runtime.

The following binding:

```yaml
- name: myweb
  project: MyWeb/Web.csproj
  bindings:
    - port: 80
    - name: management
      port: 3000
```

Will be translated into the following environment variables:

```
SERVICE_MYWEB_PORT=80
SERVICE_MYWEB_HOST=localhost
SERVICE_MYWEB_PROTOCOL=http
SERVICE_MYWEB_MANAGEMENT_PORT=3000
SERVICE_MYWEB_MANAGEMENT_HOST=localhost
SERVICE_MYWEB_MANAGEMENT_PROTOCOL=http
```

It will also be exposed via specific `IConfiguration` keys:

```
service:myweb:port=80
service:myweb:protocol=http
service:myweb:management:port=3000
service:myweb:management:protocol=http
```

### Environment

The ochestrator also injects various environment variables to communicate various pieces of information to the application:

Each binding in the list of binding for a particular service is injected into that application as an environment variable in the form `{PROTOCOL ?? "HTTP"}_PORT`. For example:

```
HTTP_PORT=5005
HTTPS_PORT=5006
```

##  The Micronetes SDK

The Micronetes SDK uses the conventions introduced by the orchestrator and introduces primitives that simplify microservice communication. The core abstraction for communicating with another microservice is an `IClientFactory<TClient>`.

```C#
public interface IClientFactory<TClient>
{
    TClient CreateClient(string name);
}
```

### Service Discovery via IConfiguration

The Micronetes SDK includes extension methods for resolving various addresses of other services:

```C#
using System;

namespace Microsoft.Extensions.Configuration
{
    public static class ConfigurationExtensions
    {
        public static Uri GetUri(this IConfiguration configuration, string name);
        public static string GetHost(this IConfiguration configuration, string name);
        public static int? GetPort(this IConfiguration configuration, string name);
        public static string GetProtocol(this IConfiguration configuration, string name);
    }
}
```

## Using CI builds

To use CI builds add the following nuget feed:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources>
        <clear />
        <add key="micronetes" value="https://f.feedz.io/davidfowl/micronetes/nuget/index.json" />
        <add key="NuGet.org" value="https://api.nuget.org/v3/index.json" />
    </packageSources>
</configuration>
```

To install the **m8s** CLI use the following command:

```
dotnet tool install m8s --version {version} -g --add-source https://f.feedz.io/davidfowl/micronetes/nuget/index.json
```

Find the list of [versions](https://f.feedz.io/davidfowl/micronetes/nuget/v3/packages/m8s/index.json)
