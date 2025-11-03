using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly string _apiKey;
        private readonly EmailAddress _fromAddress;
        private readonly SendGridClient _client;

        public EmailService(IConfiguration cfg, ILogger<EmailService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY")
                      ?? cfg.GetValue<string>("SendGrid:ApiKey"); // fallback only if you used user-secrets (not recommended in production)

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                // Fail fast and provide clear guidance for developers
                var msg = "Missing SendGrid API key. Set environment variable 'SENDGRID_API_KEY' (or use user-secrets).";
                _logger.LogError(msg);
                throw new InvalidOperationException(msg);
            }

            var from = cfg.GetValue<string>("SendGrid:From") ?? throw new InvalidOperationException("Missing SendGrid:From in configuration.");
            var fromName = cfg.GetValue<string>("SendGrid:FromName") ?? from;

            _fromAddress = new EmailAddress(from, fromName);
            _client = new SendGridClient(_apiKey);
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            if (string.IsNullOrWhiteSpace(toEmail)) throw new ArgumentException("toEmail required", nameof(toEmail));
            if (string.IsNullOrWhiteSpace(subject)) subject = "(no subject)";

            var to = new EmailAddress(toEmail);
            var plainTextContent = ConvertHtmlToPlainText(htmlBody);
            var msg = MailHelper.CreateSingleEmail(_fromAddress, to, subject, plainTextContent, htmlBody);

            try
            {
                var response = await _client.SendEmailAsync(msg).ConfigureAwait(false);

                if ((int)response.StatusCode >= 400)
                {
                    // read response body for helpful debugging info
                    var body = await response.Body.ReadAsStringAsync().ConfigureAwait(false);
                    _logger.LogError("SendGrid returned HTTP {StatusCode} sending to {Email}. Body: {Body}", response.StatusCode, toEmail, body);

                    // Optionally rethrow a custom exception so calling code can handle (or keep silent and log)
                    throw new InvalidOperationException($"SendGrid failed with status {(int)response.StatusCode}: {body}");
                }

                _logger.LogInformation("Email sent via SendGrid to {Email} (Status: {Status})", toEmail, response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending email to {Email} via SendGrid", toEmail);
                throw; // bubble up so caller can handle/fail as appropriate
            }
        }

        // Very small helper to get a fallback plaintext version of HTML
        private string ConvertHtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            // remove tags
            var text = Regex.Replace(html, "<.*?>", string.Empty);
            // decode common HTML entities
            return System.Net.WebUtility.HtmlDecode(text);
        }
    }
}
