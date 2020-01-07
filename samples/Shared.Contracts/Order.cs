using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace Shared.Contracts
{
    [DataContract]
    public class Order
    {
        [DataMember(Order = 1)]
        public Guid OrderId { get; set; }

        [DataMember(Order = 2)]
        public string UserId { get; set; }

        [DataMember(Order = 3)]
        public DateTime CreatedTime { get; set; }
    }
}
