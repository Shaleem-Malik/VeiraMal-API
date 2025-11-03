using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VeiraMal.API.Services;
using static Microsoft.IO.RecyclableMemoryStreamManager;

namespace VeiraMal.API.Controllers
{
    [ApiController]
    [Route("api/stripe")]
    public class StripeWebhookController : ControllerBase
    {
        private readonly string _webhookSecret;
        private readonly ILogger<StripeWebhookController> _logger;
        private readonly CompanyService _companyService;
        private readonly IConfiguration _cfg;
        private readonly StripeClient _stripeClient;

        public StripeWebhookController(IConfiguration cfg, ILogger<StripeWebhookController> logger, CompanyService companyService)
        {
            _cfg = cfg;
            _webhookSecret = cfg.GetValue<string>("Stripe:WebhookSecret") ?? "";
            _logger = logger;
            _companyService = companyService;
            var secretKey = cfg.GetValue<string>("Stripe:SecretKey") ?? "";
            _stripeClient = new StripeClient(secretKey);
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            // read raw body for signature verification and debug logging
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var stripeSig = Request.Headers["Stripe-Signature"].ToString();

            _logger.LogInformation("Stripe webhook received. Signature header: {Sig}. Payload length: {Len}", stripeSig?.Substring(0, Math.Min(30, stripeSig?.Length ?? 0)), json?.Length ?? 0);

            Event stripeEvent;
            try
            {
                // verify signature. If this fails, we do not process the event.
                stripeEvent = EventUtility.ConstructEvent(json, stripeSig, _webhookSecret);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stripe webhook signature verification failed. Raw payload: {Payload}", json);
                // return 400 so Stripe stops retries for bad signature
                return BadRequest(new { message = "Invalid Stripe webhook signature." });
            }

            _logger.LogInformation("Stripe event type: {Type}, id: {Id}", stripeEvent.Type, stripeEvent.Id);

            try
            {
                if (stripeEvent.Type == "checkout.session.completed")
                {
                    // Defensive: fetch session from Stripe to ensure we have expanded payment_intent and up-to-date data
                    var sessionObj = stripeEvent.Data.Object as Stripe.Checkout.Session;
                    string sessionId = sessionObj?.Id;

                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        _logger.LogWarning("checkout.session.completed but payload missing session id. Trying to parse raw JSON.");
                        // try to parse ID from data object
                        var data = stripeEvent.Data;
                        sessionId = data?.Object?.GetType().GetProperty("id")?.GetValue(data.Object)?.ToString();
                    }

                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        _logger.LogError("Unable to determine session id from webhook payload.");
                        return BadRequest(new { message = "Session id missing in webhook." });
                    }

                    // Retrieve session from Stripe (expand payment_intent, customer_details)
                    var sessionService = new SessionService(_stripeClient);
                    var session = await sessionService.GetAsync(sessionId, new SessionGetOptions
                    {
                        Expand = new List<string> { "payment_intent", "customer_details" }
                    });

                    if (session == null)
                    {
                        _logger.LogError("Stripe session {SessionId} could not be retrieved.", sessionId);
                        return StatusCode(500);
                    }

                    _logger.LogInformation("Stripe session retrieved: {SessionId} payment_status={PaymentStatus}, status={Status}", session.Id, session.PaymentStatus, session.Status);

                    // Ensure metadata includes companyId, userId, companySubscriptionId
                    if (session.Metadata == null ||
                        !session.Metadata.ContainsKey("companyId") ||
                        !session.Metadata.ContainsKey("companySubscriptionId") ||
                        !session.Metadata.ContainsKey("userId"))
                    {
                        _logger.LogError("Stripe session {SessionId} is missing required metadata. Metadata keys present: {Keys}", session.Id, session.Metadata?.Keys);
                        return BadRequest(new { message = "Session metadata incomplete." });
                    }

                    // Optionally validate payment intent/state
                    var paid = false;
                    if (session.PaymentStatus == "paid")
                    {
                        paid = true;
                    }
                    else if (session.PaymentIntentId != null)
                    {
                        // As additional defense check PaymentIntent status
                        var piService = new PaymentIntentService(_stripeClient);
                        var pi = await piService.GetAsync(session.PaymentIntentId);
                        _logger.LogInformation("PaymentIntent {PI} status: {Status}", session.PaymentIntentId, pi?.Status);
                        paid = string.Equals(pi?.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
                    }

                    if (!paid)
                    {
                        _logger.LogWarning("Checkout session {SessionId} not paid yet. payment_status={PaymentStatus}. Will not finalize onboarding yet.", session.Id, session.PaymentStatus);
                        // If you still want to finalize immediately, remove this guard. But safer to require payment succeeded.
                        return Ok(new { message = "Session received but payment not confirmed yet." });
                    }

                    // parse metadata
                    try
                    {
                        var companyId = Guid.Parse(session.Metadata["companyId"]);
                        var companySubscriptionId = Guid.Parse(session.Metadata["companySubscriptionId"]);
                        // userId stored as string - try int then Guid fallback
                        var userIdRaw = session.Metadata["userId"];
                        int userIdInt = 0;
                        Guid userIdGuid = Guid.Empty;
                        bool isGuid = false;
                        if (!int.TryParse(userIdRaw, out userIdInt))
                        {
                            isGuid = Guid.TryParse(userIdRaw, out userIdGuid);
                        }

                        if (!isGuid && userIdInt == 0)
                        {
                            _logger.LogError("UserId metadata value is invalid: {UserIdRaw}", userIdRaw);
                            return BadRequest(new { message = "Invalid userId in metadata." });
                        }

                        // Call FinalizeOnboardPaymentAsync depending on your userId type.
                        if (!isGuid)
                        {
                            await _companyService.FinalizeOnboardPaymentAsync(companyId, userIdInt, companySubscriptionId, _cfg.GetValue<string>("App:SigninUrl") ?? "http://localhost:3000/signin");
                        }
                        else
                        {
                            // If your FinalizeOnboardPaymentAsync expects int, you'd need to add overload to accept Guid user keys.
                            // For now attempt to call the int overload with 0 to cause an error you can see in logs (if your IDs are GUIDs you must change signatures).
                            _logger.LogError("Webhook contains GUID userId but service expects int. Adjust CompanyService to accept GUID user ids.");
                            return BadRequest(new { message = "Metadata userId type mismatch; expected int." });
                        }

                        _logger.LogInformation("Processed checkout.session.completed for session {SessionId}", session.Id);
                        return Ok();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to finalize onboarding for session {SessionId}", session.Id);
                        // Return 500 so Stripe retries webhook later
                        return StatusCode(500, new { message = "Failed to finalize onboarding." });
                    }
                }
                else
                {
                    // log other events, but no action required
                    _logger.LogInformation("Unhandled Stripe event type {Type}", stripeEvent.Type);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected exception processing webhook event {EventId}", stripeEvent?.Id);
                return StatusCode(500);
            }

            return Ok();
        }
    }
}
