# Micronetes

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
- **The Micronetes SDK** - This adds a layer on top of the naming conventions introduced by the orchestor.

## Micronetes CLI

The Micronetes CLI is an orchestrator that coordinates multiple applications running both locally and remotely to make developing easier. The core model is an application which is made up of several services. Here's an example of a small distributed application:

**app.yaml**

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

**micronetes run app.yaml** will run launch 2 instances of the Web and 3 of Worker and will `docker run` the rabbitmq image. There's a built-in proxy that will load balance the traffic in a round-robin matter between replicas of the various processes. It will also make those services available via environment variables following the convention described later in this section.

There is also a mini control plane built in that can be used to view the state of the services and to view the logs of the various services. As an example, you can run the following commands
- `micronetes show services` - This will show the state of all services
- `micronetes show services/web` - This will show the state of the web service
- `micronetes show logs/rabbitmq` - This will show the logs for rabbitmq

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
  projectFile: MyWeb/Web.csproj
  bindings:
    - name: default
      port: 80
      protocol: http
    - name: management
      port: 3000
      protocol: http
```

These service names are injected into the application as environment variables by the orchestrator. This allows the client code to access the address information at runtime.

The following binding:

```yaml
- name: myweb
  projectFile: MyWeb/Web.csproj
  bindings:
    - name: default
      port: 80
      protocol: http
    - name: management
      port: 3000
      protocol: http
```

Will be translated into the following `IConfiguration` keys:

```
service:myweb:port=80
service:myweb:protocol=http
service:myweb:management:port=3000
service:myweb:management:protocol=http
```

The "default" binding can be accesed by the name of the service. To access other addresses, the full name must be accessed:

e.g.

```C#
// Access the default port of the MyWeb service
IClientFactory<HttpClient> clientFactory = ...
var defaultClient = clientFactory.CreateClient("myweb");

// Access the management port of the MyWeb service
var managementClient = clientFactory.CreateClient("myweb:management");
```

Most services will have a single binding so accessing them by service name directly should work.

##  The Micronetes SDK

The Micronetes SDK uses the conventions introduced by the orchestrator and introduces primitives that simplify microservice communication. The core abstraction for communicating with another microservice is an `IClientFactory<TClient>`.

```C#
public interface IClientFactory<TClient>
{
    TClient CreateClient(string name);
}
```

### Client abstractions

- HTTP - `HttpClient`
- PubSub - `PubSubClient`
- Queue - TBD
- RPC - TBD

The intent is to make decouple service addresses from the implementation. There are 2 flavours of TClient:

1. TClient can be an abstraction. 
2. TClient can be a concrete client implementation (like ConnectionMultipler).
