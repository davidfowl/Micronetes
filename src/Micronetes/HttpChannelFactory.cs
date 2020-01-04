using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Micronetes
{
    public class HttpChannelFactory : IClientFactory<Channel<byte[]>>
    {
        public HttpChannelFactory(IHttpClientFactory httpClient)
        {

        }

        public Channel<byte[]> CreateClient(string name)
        {
            throw new NotImplementedException();
        }
    }
}
