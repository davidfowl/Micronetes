# Micronetes

.NET Microservice communication made easy. This project is trying to tackle a couple of things:
- Have standard interfaces for common communication paradigms.
   - PubSub
   - Queues
   - RPC
   - HTTP
- Abstract the idea of a service address.
- Explore what it means to run a multi-project application locally.
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
- Queue - `Channel<T>/QueueClient`
- RPC - `IRpcInvoker`

The intent is to make decouple service addresses from the implementation. There are 2 flavours of TClient:

1. TClient can be an abstraction. 
2. TClient can be a concrete client implementation (like ConnectionMultipler).

## Service Descriptions

A service description is composed a service name and a list of bindings. Each binding has a name an address and a protocol.

```
ServiceName
 - (Name, Address, Protocol)
```

Examples:

```
Dapr
 - (default, 127.0.0.1:80, dapr)

MyWeb
 - (default, http://127.0.0.1:80, http)
 - (management, http://127.0.0.1:3000, http)

Pubbie
  - (default, 127.0.0.1:5005, pubbie)

Redis 
  - (default, 127.0.0.1:6379, redis)

Database 
 - (default, mongodb://localhost, mongodb)
 - (default, Data Source=.;Initial Catalog=DB name;Integrated Security=True;MultipleActiveResultSets=True, sqlserver)

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

## Protocols

The `IClientFactory<T>` implementation must understand protocol names directly. For example, the the `PubSubClientFactory` has to understand the redis protocol in order to speak to an endpoint.


## Open Questions

- These service definitions don't discuss the actual contracts being exposed:
   - .proto files
   - swagger files

The caller is expected to understand that contract.
- Who stores the mapping from protocol to `IClientFactory<T>` implementation?
- How does this addressing scheme work for accessing clients with an identity? SignalR connection, virtual actor etc.
   - Do we make the service a router that does further dispatch?