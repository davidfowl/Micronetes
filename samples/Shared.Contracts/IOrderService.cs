using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Contracts
{
    [ServiceContract]
    public interface IOrderService
    {
        ValueTask PlaceOrderAsync(Order order);
    }
}
