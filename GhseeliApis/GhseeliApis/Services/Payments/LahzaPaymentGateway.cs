using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Services.Payments;

public static class PaymentProviders
{
    public const string Lahza = "Lahza";
}

public sealed class LahzaConfigurationOptions
{
    public const string SectionName = "Lahza";

    public string BaseUrl { get; set; } = "https://api.lahza.io";
    public string SecretKey { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
}

public sealed class LahzaConfigurationOptionsValidator :
    IValidateOptions<LahzaConfigurationOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        LahzaConfigurationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            return ValidateOptionsResult.Success;
        }
        if (!Uri.TryCreate(options.BaseUrl?.Trim(), UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return ValidateOptionsResult.Fail(
                "Lahza:BaseUrl must be an absolute HTTPS URL when Lahza is enabled.");
        }
        if (!string.IsNullOrWhiteSpace(options.CallbackUrl) &&
            (!Uri.TryCreate(
                 options.CallbackUrl.Trim(),
                 UriKind.Absolute,
                 out var callbackUri) ||
             callbackUri.Scheme != Uri.UriSchemeHttps))
        {
            return ValidateOptionsResult.Fail(
                "Lahza:CallbackUrl must be an absolute HTTPS URL when configured.");
        }
        if (options.TimeoutSeconds is < 1 or > 60)
        {
            return ValidateOptionsResult.Fail(
                "Lahza:TimeoutSeconds must be between 1 and 60.");
        }
        return ValidateOptionsResult.Success;
    }
}

public sealed record PaymentInitializationCommand(
    Guid PaymentId,
    Guid BookingId,
    Guid BookingReference,
    long Amount,
    string Currency,
    string CustomerEmail,
    string ProviderReference);

public sealed record PaymentInitializationResult(
    string ProviderReference,
    string Status,
    Uri CheckoutUrl,
    long Amount,
    string Currency);

public sealed record PaymentVerificationResult(
    string ProviderReference,
    string Status,
    string? ProviderTransactionId,
    long Amount,
    string Currency);

public interface IPaymentGateway
{
    Task<PaymentInitializationResult> InitializeAsync(
        PaymentInitializationCommand command,
        CancellationToken cancellationToken);

    Task<PaymentVerificationResult> VerifyAsync(
        string providerReference,
        CancellationToken cancellationToken);
}

public sealed class LahzaPaymentGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<LahzaConfigurationOptions> _options;

    public LahzaPaymentGateway(
        HttpClient httpClient,
        IOptionsMonitor<LahzaConfigurationOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<PaymentInitializationResult> InitializeAsync(
        PaymentInitializationCommand command,
        CancellationToken cancellationToken)
    {
        var options = GetConfiguredOptions();
        var payload = new LahzaInitializeRequest
        {
            Amount = command.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Email = command.CustomerEmail,
            Currency = command.Currency,
            Reference = command.ProviderReference,
            CallbackUrl = NullIfWhiteSpace(options.CallbackUrl),
            Channels = ["card"],
            Metadata = JsonSerializer.Serialize(
                new
                {
                    payment_id = command.PaymentId,
                    booking_id = command.BookingId,
                    booking_reference = command.BookingReference
                },
                JsonOptions)
        };

        using var request = CreateRequest(
            HttpMethod.Post,
            BuildUri(options.BaseUrl, "transaction/initialize"),
            options.SecretKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await SendAsync(request, cancellationToken);
        var envelope = await ReadEnvelopeAsync<LahzaInitializeData>(
            response,
            cancellationToken);
        var data = envelope.Data
            ?? throw GatewayContractFailure();
        if (!Uri.TryCreate(data.AuthorizationUrl, UriKind.Absolute, out var checkoutUrl) ||
            checkoutUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(
                data.Reference,
                command.ProviderReference,
                StringComparison.Ordinal))
        {
            throw GatewayContractFailure();
        }

        return new PaymentInitializationResult(
            data.Reference,
            "initialized",
            checkoutUrl,
            command.Amount,
            command.Currency);
    }

    public async Task<PaymentVerificationResult> VerifyAsync(
        string providerReference,
        CancellationToken cancellationToken)
    {
        var options = GetConfiguredOptions();
        using var request = CreateRequest(
            HttpMethod.Get,
            BuildUri(
                options.BaseUrl,
                $"transaction/verify/{Uri.EscapeDataString(providerReference)}"),
            options.SecretKey);
        using var response = await SendAsync(request, cancellationToken);
        var envelope = await ReadEnvelopeAsync<LahzaTransactionData>(
            response,
            cancellationToken);
        var data = envelope.Data
            ?? throw GatewayContractFailure();
        if (string.IsNullOrWhiteSpace(data.Reference) ||
            string.IsNullOrWhiteSpace(data.Status) ||
            string.IsNullOrWhiteSpace(data.Currency))
        {
            throw GatewayContractFailure();
        }

        return new PaymentVerificationResult(
            data.Reference,
            data.Status,
            data.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            data.Amount,
            data.Currency.ToUpperInvariant());
    }

    private LahzaConfigurationOptions GetConfiguredOptions()
    {
        var options = _options.CurrentValue;
        if (!LahzaConfiguration.IsConfigured(options))
        {
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.ProviderUnavailable);
        }
        return options;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.GatewayAmbiguous,
                inner: exception);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException)
        {
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.GatewayAmbiguous,
                inner: exception);
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri uri,
        string secretKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            secretKey.Trim());
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static Uri BuildUri(string baseUrl, string relativePath) =>
        new(new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute), relativePath);

    private static async Task<LahzaEnvelope<T>> ReadEnvelopeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.GatewayAmbiguous);
        }

        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var boundedBody = new MemoryStream();
            var buffer = new byte[8192];
            const int maxResponseBytes = 262_144;
            while (true)
            {
                var read = await body.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }
                if (boundedBody.Length + read > maxResponseBytes)
                {
                    throw GatewayContractFailure();
                }
                await boundedBody.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            boundedBody.Position = 0;
            var envelope = await JsonSerializer.DeserializeAsync<LahzaEnvelope<T>>(
                boundedBody,
                JsonOptions,
                cancellationToken);
            if (envelope is null || !envelope.Status)
            {
                throw GatewayContractFailure();
            }
            return envelope;
        }
        catch (JsonException exception)
        {
            throw GatewayContractFailure(exception);
        }
    }

    private static CustomerPaymentException GatewayContractFailure(
        Exception? inner = null) =>
        new(
            502,
            CustomerPaymentErrorCodes.GatewayAmbiguous,
            inner: inner);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class LahzaInitializeRequest
    {
        public string Amount { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        [JsonPropertyName("callback_url")]
        public string? CallbackUrl { get; set; }
        public string[] Channels { get; set; } = [];
        public string Metadata { get; set; } = string.Empty;
    }

    private sealed class LahzaEnvelope<T>
    {
        public bool Status { get; set; }
        public T? Data { get; set; }
    }

    private sealed class LahzaInitializeData
    {
        [JsonPropertyName("authorization_url")]
        public string AuthorizationUrl { get; set; } = string.Empty;
        [JsonPropertyName("access_code")]
        public string AccessCode { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }

    private sealed class LahzaTransactionData
    {
        public long? Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public long Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
    }
}

public static class LahzaConfiguration
{
    public static bool IsConfigured(LahzaConfigurationOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl?.Trim(), UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(options.SecretKey))
        {
            return false;
        }

        var secret = options.SecretKey.Trim();
        return !secret.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) &&
               !secret.Contains("_HERE", StringComparison.OrdinalIgnoreCase);
    }
}

public enum PaymentEventKind
{
    Succeeded,
    Failed,
    Canceled,
    RefundPending,
    RefundProcessing,
    Refunded,
    RefundFailed,
    Ignored
}

public sealed record VerifiedPaymentEvent(
    string EventId,
    string EventType,
    PaymentEventKind Kind,
    string ProviderReference,
    string? ProviderTransactionId,
    long Amount,
    string Currency);

public interface IPaymentWebhookParser
{
    VerifiedPaymentEvent Parse(
        ReadOnlyMemory<byte> rawBody,
        string signature,
        string secretKey);
}

public sealed class LahzaWebhookParser : IPaymentWebhookParser
{
    public VerifiedPaymentEvent Parse(
        ReadOnlyMemory<byte> rawBody,
        string signature,
        string secretKey)
    {
        VerifySignature(rawBody, signature, secretKey);

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var eventType = RequiredString(root, "event");
            var data = root.GetProperty("data");
            var transaction = data.TryGetProperty("transaction", out var transactionValue) &&
                              transactionValue.ValueKind == JsonValueKind.Object
                ? transactionValue
                : data;
            var reference = RequiredString(transaction, "reference");
            var transactionId = TryGetIdentifier(transaction, "id");
            var amount = TryGetInt64(data, "amount") ??
                         TryGetInt64(transaction, "amount") ??
                         0;
            var currency = TryGetString(data, "currency") ??
                           TryGetString(transaction, "currency") ??
                           string.Empty;
            var eventId = TryGetString(root, "id") ??
                          $"{eventType}:{TryGetIdentifier(data, "id") ?? transactionId ?? reference}";

            return new VerifiedPaymentEvent(
                eventId,
                eventType,
                MapKind(eventType),
                reference,
                transactionId,
                amount,
                currency.ToUpperInvariant());
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new CustomerPaymentException(
                400,
                CustomerPaymentErrorCodes.EventInvalid,
                inner: exception);
        }
    }

    private static void VerifySignature(
        ReadOnlyMemory<byte> rawBody,
        string signature,
        string secretKey)
    {
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature.Trim());
        }
        catch (FormatException exception)
        {
            throw new CustomerPaymentException(
                400,
                CustomerPaymentErrorCodes.SignatureInvalid,
                inner: exception);
        }

        var expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secretKey),
            rawBody.Span);
        if (supplied.Length != expected.Length ||
            !CryptographicOperations.FixedTimeEquals(supplied, expected))
        {
            throw new CustomerPaymentException(
                400,
                CustomerPaymentErrorCodes.SignatureInvalid);
        }
    }

    private static PaymentEventKind MapKind(string eventType) =>
        eventType switch
        {
            "charge.success" => PaymentEventKind.Succeeded,
            "refund.pending" => PaymentEventKind.RefundPending,
            "refund.processing" => PaymentEventKind.RefundProcessing,
            "refund.processed" => PaymentEventKind.Refunded,
            "refund.failed" => PaymentEventKind.RefundFailed,
            _ => PaymentEventKind.Ignored
        };

    private static string RequiredString(JsonElement element, string name) =>
        TryGetString(element, name)
        ?? throw new InvalidOperationException($"Required Lahza field '{name}' is missing.");

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind == JsonValueKind.Number
                ? value.GetRawText()
                : null;
    }

    private static string? TryGetIdentifier(JsonElement element, string name) =>
        TryGetString(element, name);

    private static long? TryGetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var result)
            ? result
            : null;
}
