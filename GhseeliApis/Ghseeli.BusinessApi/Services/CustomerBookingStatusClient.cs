using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Ghseeli.BusinessApi.Services;

public sealed class CustomerBookingStatusClientOptions
{
    public const string SectionName = "CustomerBookingStatusClient";
    public string BaseUrl { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string ActiveSecret { get; set; } = string.Empty;
    public bool RequireHttps { get; set; } = true;
    public bool AllowInsecureHttpInDevelopment { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
    public bool DisableDeliveryInTesting { get; set; }
}

public sealed class CustomerBookingStatusClientOptionsValidator :
    IValidateOptions<CustomerBookingStatusClientOptions>
{
    private readonly IWebHostEnvironment _environment;

    public CustomerBookingStatusClientOptionsValidator(IWebHostEnvironment environment) =>
        _environment = environment;

    public ValidateOptionsResult Validate(
        string? name,
        CustomerBookingStatusClientOptions options)
    {
        if (options.DisableDeliveryInTesting)
        {
            return _environment.IsEnvironment("Testing")
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    "CustomerBookingStatusClient:DisableDeliveryInTesting is allowed only in Testing.");
        }
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp &&
             baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return ValidateOptionsResult.Fail(
                "CustomerBookingStatusClient:BaseUrl must be an absolute URL.");
        }
        if (!InternalServiceHeaderValueValidator.IsValidServiceId(options.ServiceId))
        {
            return ValidateOptionsResult.Fail(
                "CustomerBookingStatusClient:ServiceId is invalid.");
        }
        if (string.IsNullOrWhiteSpace(options.ActiveSecret) ||
            options.ActiveSecret.Length < 32)
        {
            return ValidateOptionsResult.Fail(
                "CustomerBookingStatusClient:ActiveSecret must contain at least 32 characters.");
        }
        if (options.RequireHttps &&
            !(_environment.IsDevelopment() && options.AllowInsecureHttpInDevelopment) &&
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return ValidateOptionsResult.Fail(
                "CustomerBookingStatusClient:BaseUrl must use HTTPS.");
        }
        return ValidateOptionsResult.Success;
    }
}

public interface ICustomerBookingStatusClient
{
    Task DeliverAsync(
        string requestJson,
        Guid eventId,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class CustomerBookingStatusClient : ICustomerBookingStatusClient
{
    private readonly HttpClient _client;
    private readonly CustomerBookingStatusClientOptions _options;
    private readonly IWebHostEnvironment _environment;

    public CustomerBookingStatusClient(
        HttpClient client,
        IOptions<CustomerBookingStatusClientOptions> options,
        IWebHostEnvironment environment)
    {
        _client = client;
        _options = options.Value;
        _environment = environment;
    }

    public async Task DeliverAsync(
        string requestJson,
        Guid eventId,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty ||
            !InternalServiceHeaderValueValidator.IsValidIdempotencyKey(idempotencyKey))
        {
            throw new ArgumentException("The callback transport identity is invalid.");
        }
        var uri = ResolveUri();
        var body = Encoding.UTF8.GetBytes(requestJson);
        var timestamp = DateTime.UtcNow.ToString("O");
        var nonce = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
        var canonical = InternalServiceCanonicalRequest.Build(
            _options.ServiceId,
            "POST",
            uri.AbsolutePath,
            Array.Empty<KeyValuePair<string, string?>>(),
            timestamp,
            nonce,
            idempotencyKey,
            InternalServiceCanonicalRequest.ComputeSha256Hex(body));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.ActiveSecret));
        var signature = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.TryAddWithoutValidation(InternalServiceWireConstants.ServiceIdHeaderName, _options.ServiceId);
        request.Headers.TryAddWithoutValidation(InternalServiceWireConstants.TimestampHeaderName, timestamp);
        request.Headers.TryAddWithoutValidation(InternalServiceWireConstants.NonceHeaderName, nonce);
        request.Headers.TryAddWithoutValidation(InternalServiceWireConstants.SignatureHeaderName, signature);
        request.Headers.TryAddWithoutValidation(InternalServiceWireConstants.IdempotencyKeyHeaderName, idempotencyKey);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.CorrelationIdHeaderName,
            InternalServiceHeaderValueValidator.GetOrCreateCorrelationId(correlationId));
        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Customer booking status callback returned {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }
    }

    private Uri ResolveUri()
    {
        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            !InternalServiceHeaderValueValidator.IsValidServiceId(_options.ServiceId) ||
            _options.ActiveSecret.Length < 32)
        {
            throw new InvalidOperationException("Customer booking status client is not configured.");
        }
        if (_options.RequireHttps &&
            !(_environment.IsDevelopment() && _options.AllowInsecureHttpInDevelopment) &&
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Customer booking status callback requires HTTPS.");
        }
        return new Uri(baseUri, "/api/v1/internal/bookings/status");
    }
}
