// Services/StripeService.cs
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VeiraMal.API.Services
{
    public interface IStripeService
    {
        /// <summary>
        /// Create a one-time payment Checkout Session (used when you charge a fixed amount in cents).
        /// </summary>
        Task<Session> CreateCheckoutSessionAsync(
            Guid companyId,
            int userId,
            Guid companySubscriptionId,
            long amountInCents,
            string successUrl,
            string cancelUrl,
            string currency = "aud");

        /// <summary>
        /// Create a subscription Checkout Session using an existing Stripe Price ID (for recurring billing).
        /// </summary>
        Task<Session> CreateCheckoutSessionForSubscriptionAsync(
            Guid companyId,
            Guid companySubscriptionId,
            int userId,
            string customerEmail,
            string priceId,
            string successUrl,
            string cancelUrl);
    }

    public class StripeService : IStripeService
    {
        private readonly IConfiguration _cfg;
        private readonly StripeClient _client;

        public StripeService(IConfiguration cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            var secret = _cfg.GetValue<string>("Stripe:SecretKey");
            if (string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("Stripe:SecretKey not configured.");

            _client = new StripeClient(secret);
        }

        /// <summary>
        /// Create a one-time payment Checkout Session (payment mode).
        /// </summary>
        public async Task<Session> CreateCheckoutSessionAsync(
            Guid companyId,
            int userId,
            Guid companySubscriptionId,
            long amountInCents,
            string successUrl,
            string cancelUrl,
            string currency = "aud")
        {
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                Mode = "payment",
                SuccessUrl = successUrl + "?session_id={CHECKOUT_SESSION_ID}",
                CancelUrl = cancelUrl,
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = currency,
                            UnitAmount = amountInCents,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = "VeiraMal subscription - initial payment",
                                Description = $"CompanyId:{companyId} - subscription initial payment"
                            }
                        }
                    }
                },
                Metadata = new Dictionary<string, string>
                {
                    { "companyId", companyId.ToString() },
                    { "userId", userId.ToString() },
                    { "companySubscriptionId", companySubscriptionId.ToString() }
                }
            };

            var service = new SessionService(_client);
            var session = await service.CreateAsync(options);
            return session;
        }

        /// <summary>
        /// Creates a Stripe Checkout Session for subscriptions (expects an existing Stripe Price ID configured).
        /// </summary>
        public async Task<Session> CreateCheckoutSessionForSubscriptionAsync(
            Guid companyId,
            Guid companySubscriptionId,
            int userId,
            string customerEmail,
            string priceId,
            string successUrl,
            string cancelUrl)
        {
            var options = new SessionCreateOptions
            {
                Mode = "subscription",
                CustomerEmail = customerEmail, // Stripe will create a customer for us
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = priceId,
                        Quantity = 1
                    }
                },
                SuccessUrl = successUrl + "?session_id={CHECKOUT_SESSION_ID}",
                CancelUrl = cancelUrl,
                Metadata = new Dictionary<string, string>
                {
                    { "companyId", companyId.ToString() },
                    { "companySubscriptionId", companySubscriptionId.ToString() },
                    { "userId", userId.ToString() } // include userId so webhook can finalize onboarding
                }
            };

            var service = new SessionService(_client);
            var session = await service.CreateAsync(options);
            return session;
        }
    }
}
