# Micronetes

.NET Microservices made easy. This project is trying to tackle a couple of things:
- Have standard interfaces for common communication paradigms.
   - PubSub
   - Queues
   - RPC
   - HTTP
- Abstract the idea of a service address.
- Explore what it means to run a the microservice application locally (multiple projects with service discovery)
- Explore what it means to deploy this application to kubernetes without changes to the code and have it just work.

The core interface for client -> service communication the `IClientFactory<TClient>`:

```C#
public interface IClientFactory<TClient>
{
    TClient CreateClient(string name);
}
```

## Client abstractions

- HTTP - `HttpClient`
- PubSub - `PubSubClient`
- Queue - ???
- RPC - ???

The intent is to make decouple service addresses from the implementation. There are 2 flavours of TClient:

1. TClient can be an abstraction. 
2. TClient can be a concrete client implementation (like ConnectionMultipler).

## Service Descriptions

A service description is yaml file with list of services. Services can have multiple bindings that describe how the application can connect to it.

```yaml
- name: redis
  dockerImage: redis:5
  bindings:
    - name: default
      port: 6379
      protocol: redis
```

Examples:

```yaml
- name: dapr
  executable: daprd
  bindings:
    - name: default
      port: 80
      protocol: dapr
```

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

```yaml
- name: db
  dockerImage: redis:5
  bindings:
    - name: default
      connectionString: Data Source=.;Initial Catalog=DB name;Integrated Security=True;
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
myweb:service:port=80
myweb:service:protocol=http
myweb:service:management:port=3000
myweb:service:management:protocol=http
```

The "default" binding can be accesed by the name of the service. To access other addresses, the full name must be accessed:

e.g.

```C#
// Access the default port of the MyWeb service
IClientFactory<HttpClient> clientFactory = ...
var defaultClient = clientFactory.CreateClient("MyWeb");

// Access the default port of the MyWeb service explictly
defaultClient = clientFactory.CreateClient("MyWeb/default");

// Access the management port of the MyWeb service
var managementClient = clientFactory.CreateClient("MyWeb/management");
```

Most services will have a single binding so accessing them by service name directly should work.
