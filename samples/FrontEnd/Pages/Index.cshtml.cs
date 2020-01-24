using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shared.Contracts;

namespace FrontEnd.Pages
{
    public class IndexModel : PageModel
    {
        public void OnGet()
        {

        }

        public async Task<IActionResult> OnPost([FromServices]IOrderService client)
        {
            var order = new Order
            {
                OrderId = Guid.NewGuid(),
                CreatedTime = DateTime.UtcNow,
                UserId = User.Identity.Name
            };

            await client.PlaceOrderAsync(order);

            return Redirect("/");
        }
    }
}
