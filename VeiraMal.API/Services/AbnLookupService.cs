using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VeiraMal.API.DTOs;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class AbnLookupOptions
    {
        public string AuthenticationGuid { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
    }

    public class AbnLookupService : IAbnLookupService
    {
        private readonly HttpClient _httpClient;
        private readonly AbnLookupOptions _options;
        private readonly ILogger<AbnLookupService> _logger;

        public AbnLookupService(
            HttpClient httpClient,
            IOptions<AbnLookupOptions> options,
            ILogger<AbnLookupService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<AbnValidationResultDto> ValidateAbnAsync(string abn, CancellationToken cancellationToken = default)
        {
            var cleaned = new string((abn ?? string.Empty).Where(char.IsDigit).ToArray());

            if (cleaned.Length != 11)
                return new AbnValidationResultDto
                {
                    IsValid = false,
                    Message = "ABN must be exactly 11 digits."
                };

            if (!IsValidAbnChecksum(cleaned))
                return new AbnValidationResultDto
                {
                    IsValid = false,
                    Message = "ABN format is invalid."
                };

            var endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
                ? "https://abr.business.gov.au/ABRXMLSearch/AbrXmlSearch.asmx"
                : _options.Endpoint.Trim();

            var soapEnvelope = BuildSoapEnvelope(cleaned);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.TryAddWithoutValidation(
                    "SOAPAction",
                    "\"http://abr.business.gov.au/ABRXMLSearch/ABRSearchByABN\"");
                request.Content = new StringContent(soapEnvelope, System.Text.Encoding.UTF8, "text/xml");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var xml = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogInformation("ABN lookup HTTP {StatusCode} for {ABN}", (int)response.StatusCode, cleaned);

                if (string.IsNullOrWhiteSpace(xml))
                {
                    return new AbnValidationResultDto
                    {
                        IsValid = false,
                        Message = "ABN Lookup returned an empty response."
                    };
                }

                if (xml.Contains("Fault", StringComparison.OrdinalIgnoreCase) ||
                    xml.Contains("soap:Fault", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("ABN lookup returned SOAP fault for {ABN}: {Xml}", cleaned, xml);
                    return new AbnValidationResultDto
                    {
                        IsValid = false,
                        Message = "ABN Lookup returned a fault response."
                    };
                }

                if (xml.Contains("GUID", StringComparison.OrdinalIgnoreCase) &&
                    (xml.Contains("not", StringComparison.OrdinalIgnoreCase) ||
                     xml.Contains("invalid", StringComparison.OrdinalIgnoreCase)))
                {
                    return new AbnValidationResultDto
                    {
                        IsValid = false,
                        Message = "ABN Lookup authentication GUID is invalid."
                    };
                }

                var doc = XDocument.Parse(xml);

                string? entityName =
                    GetFirstValue(doc, "organisationName") ??
                    GetFirstValue(doc, "tradingName") ??
                    GetFirstValue(doc, "legalName") ??
                    GetFirstValue(doc, "mainName") ??
                    GetFirstValue(doc, "entityName") ??
                    GetFirstValue(doc, "EntityName");

                string? status =
                    GetFirstValue(doc, "entityStatusCode") ??
                    GetFirstValue(doc, "entityStatus") ??
                    GetFirstValue(doc, "EntityStatus") ??
                    GetFirstValue(doc, "EntityStatusCode");

                string? isCurrent =
                    GetFirstValue(doc, "isCurrentIndicator") ??
                    GetFirstValue(doc, "IsCurrentIndicator");

                string? returnedAbn =
                    GetFirstValue(doc, "identifierValue") ??
                    GetFirstValue(doc, "ABN") ??
                    GetFirstValue(doc, "abn");

                var looksValid =
                    string.Equals(isCurrent, "Y", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(entityName) ||
                    !string.IsNullOrWhiteSpace(returnedAbn);

                if (!looksValid)
                {
                    return new AbnValidationResultDto
                    {
                        IsValid = false,
                        Message = "ABN could not be validated.",
                        Abn = cleaned
                    };
                }

                return new AbnValidationResultDto
                {
                    IsValid = true,
                    Message = "ABN validated successfully.",
                    Abn = cleaned,
                    EntityName = entityName,
                    Status = status
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "ABN lookup request failed for {ABN}", cleaned);
                return new AbnValidationResultDto
                {
                    IsValid = false,
                    Message = "ABN Lookup service is unavailable right now."
                };
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "ABN lookup timed out for {ABN}", cleaned);
                return new AbnValidationResultDto
                {
                    IsValid = false,
                    Message = "ABN Lookup timed out."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ABN lookup failed for {ABN}", cleaned);
                return new AbnValidationResultDto
                {
                    IsValid = false,
                    Message = "ABN could not be validated."
                };
            }
        }

        private string BuildSoapEnvelope(string abn) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
        <soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
                       xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
                       xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
          <soap:Body>
            <ABRSearchByABN xmlns=""http://abr.business.gov.au/ABRXMLSearch/"">
              <searchString>{System.Security.SecurityElement.Escape(abn)}</searchString>
              <includeHistoricalDetails>N</includeHistoricalDetails>
              <authenticationGuid>{System.Security.SecurityElement.Escape(_options.AuthenticationGuid)}</authenticationGuid>
            </ABRSearchByABN>
          </soap:Body>
        </soap:Envelope>";

        private static string? GetFirstValue(XDocument doc, string localName)
        {
            return doc.Descendants()
                .FirstOrDefault(x => x.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }

        // Official ABN checksum logic
        private static bool IsValidAbnChecksum(string abn)
        {
            int[] weights = { 10, 1, 3, 5, 7, 9, 11, 13, 15, 17, 19 };
            int sum = 0;

            int first = abn[0] - '0';
            if (first < 1) return false;

            sum += (first - 1) * weights[0];

            for (int i = 1; i < 11; i++)
                sum += (abn[i] - '0') * weights[i];

            return sum % 89 == 0;
        }
    }
}