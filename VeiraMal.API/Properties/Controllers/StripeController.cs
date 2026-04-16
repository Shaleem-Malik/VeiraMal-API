// Controllers/StripeController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeiraMal.API.Services;

namespace VeiraMal.API.Controllers
{
    [ApiController]
    [Route("api/stripe")]
    public class StripeController : ControllerBase
    {
        private readonly string _stripeSecret;
        private readonly IStripeService _stripeService;
        private readonly ILogger<StripeController> _logger;

        public StripeController(IConfiguration cfg, IStripeService stripeService, ILogger<StripeController> logger)
        {
            _stripeService = stripeService;
            _logger = logger;
            _stripeSecret = cfg.GetValue<string>("Stripe:SecretKey") ?? "";
            Stripe.StripeConfiguration.ApiKey = _stripeSecret;
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

        public class CreateSessionRequest
        {
            public string? CompanyId { get; set; }               // GUID string (required)
            public string? CompanySubscriptionId { get; set; }   // GUID string (required)
            public int UserId { get; set; }                     // integer user id
            public long? AmountInCents { get; set; }            // for one-time payments (optional if using price data)
            public string Currency { get; set; } = "aud";
            public bool IsSubscription { get; set; } = false;   // subscription vs one-time
            public string? PriceId { get; set; }                 // required for subscription mode (Stripe Price ID)
            public string? CustomerEmail { get; set; }           // for subscription mode (optional)
            public string? SuccessUrl { get; set; }
            public string? CancelUrl { get; set; }
        }

        [HttpPost("create-session")]
        public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest req)
        {
            if (req == null) return BadRequest(new { message = "Request body required." });

            if (!System.Guid.TryParse(req.CompanyId ?? "", out var companyGuid))
                return BadRequest(new { message = "Invalid CompanyId." });

            if (!System.Guid.TryParse(req.CompanySubscriptionId ?? "", out var companySubscriptionGuid))
                return BadRequest(new { message = "Invalid CompanySubscriptionId." });

            if (req.IsSubscription)
            {
                if (string.IsNullOrWhiteSpace(req.PriceId))
                    return BadRequest(new { message = "PriceId is required for subscriptions." });

                var session = await _stripeService.CreateCheckoutSessionForSubscriptionAsync(
                    companyGuid,
                    companySubscriptionGuid,
                    req.UserId,
                    req.CustomerEmail,
                    req.PriceId,
                    req.SuccessUrl,
                    req.CancelUrl
                );

                return Ok(new { sessionId = session.Id, url = session.Url });
            }
            else
            {
                if (!req.AmountInCents.HasValue || req.AmountInCents.Value <= 0)
                    return BadRequest(new { message = "AmountInCents is required for one-time payments." });

                var session = await _stripeService.CreateCheckoutSessionAsync(
                    companyGuid,
                    req.UserId,
                    companySubscriptionGuid,
                    req.AmountInCents.Value,
                    req.SuccessUrl,
                    req.CancelUrl,
                    req.Currency
                );

                return Ok(new { sessionId = session.Id, url = session.Url });
            }
        }
    }
}
