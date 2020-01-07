using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Micronetes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Shared.Contracts;

namespace FrontEnd.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {

        }

        public async Task<IActionResult> OnPost([FromServices]IClientFactory<IOrderService> clientFactory)
        {
            var client = clientFactory.CreateClient("backend");
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
