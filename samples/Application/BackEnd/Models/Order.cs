using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BackEnd.Models
{
    public class Order
    {
        public Guid OrderId { get; set; }

        public string UserId { get; set; }

        public DateTime CreatedTime { get; set; }
    }
}
