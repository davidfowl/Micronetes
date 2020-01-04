## Micronetes

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

The intent is to make decouple service addresses from the implementation. There are 2 flavours of TClient:

1. TClient can be an abstraction. 
2. TClient can be a concrete client implementation (like ConnectionMultipler).