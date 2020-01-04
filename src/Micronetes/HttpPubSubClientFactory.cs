using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Micronetes
{
    public class HttpPubSubClientFactory : IClientFactory<PubSubClient>
    {
        public HttpPubSubClientFactory(IHttpClientFactory httpClient)
        {

        }

        public PubSubClient CreateClient(string name)
        {
            throw new NotImplementedException();
        }
    }
}
