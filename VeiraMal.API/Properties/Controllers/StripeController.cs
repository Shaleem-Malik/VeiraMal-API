// Controllers/StripeController.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;

namespace VeiraMal.API.Controllers
{
    [ApiController]
    [Route("api/stripe")]
    public class StripeController : ControllerBase
    {
        private readonly string _stripeSecret;

        public StripeController(IConfiguration cfg)
        {
            _stripeSecret = cfg.GetValue<string>("Stripe:SecretKey") ?? "";
            StripeConfiguration.ApiKey = _stripeSecret;
        }

        // GET /api/stripe/session?sessionId=cs_...
        [HttpGet("session")]
        public async Task<IActionResult> GetSession([FromQuery] string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return BadRequest(new { message = "sessionId query parameter is required." });

            var service = new SessionService();
            var session = await service.GetAsync(sessionId, new SessionGetOptions
            {
                Expand = new List<string> { "payment_intent", "customer_details" }
            });

            if (session == null)
                return NotFound(new { message = "Stripe session not found." });

            return Ok(new
            {
                sessionId = session.Id,
                sessionMode = session.Mode,
                paymentStatus = session.PaymentStatus,
                status = session.Status,
                amountTotal = session.AmountTotal,
                currency = session.Currency,
                customerEmail = session.CustomerDetails?.Email,
                metadata = session.Metadata
            });
        }
    }
}
