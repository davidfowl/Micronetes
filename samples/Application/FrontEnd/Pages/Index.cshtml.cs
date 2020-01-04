using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FrontEnd.Models;
using Micronetes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

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

        public async Task<IActionResult> OnPost([FromServices]IClientFactory<HttpClient> clientFactory)
        {
            var client = clientFactory.CreateClient("backend");
            var order = JsonSerializer.Serialize(new Order
            {
                OrderId = Guid.NewGuid(),
                CreatedTime = DateTime.UtcNow,
                UserId = User.Identity.Name
            });

            var response = await client.PostAsync("/orders", new StringContent(order));
            response.EnsureSuccessStatusCode();

            return Redirect("/");
        }
    }
}
