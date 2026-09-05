using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ghseeli.IntegrationContracts.InternalHttp;

public static class BusinessCatalogContract
{
    public const string Version = "v1";

    public static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(
            namingPolicy: null,
            allowIntegerValues: false));

        return options;
    }
}

public static class InternalServiceWireConstants
{
    public const string SignatureVersion = "ghseeli-hmac-sha256-v1";
    public const string ServiceIdHeaderName = "X-Ghseeli-Service-Id";
    public const string TimestampHeaderName = "X-Ghseeli-Timestamp";
    public const string NonceHeaderName = "X-Ghseeli-Nonce";
    public const string SignatureHeaderName = "X-Ghseeli-Signature";
    public const string CorrelationIdHeaderName = "X-Correlation-Id";
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    public const int MaxServiceIdLength = 64;
    public const int MaxCorrelationIdLength = 64;
    public const int MaxNonceLength = 128;
    public const int MinNonceLength = 32;
    public const int MaxIdempotencyKeyLength = 128;
    public const int MaxRequestBodyBytes = 65_536;
    public const int MaxStoredResponseBytes = 65_536;
}

public static class InternalServiceOperationNames
{
    public const string CatalogSnapshot = "catalog_snapshot";
    public const string AppointmentValidate = "appointment_validate";
    public const string AppointmentAvailableSlots = "appointment_available_slots";
    public const string ReservationCreate = "reservation_create";
    public const string ReservationStatusRead = "reservation_status_read";
    public const string BookingStatusCallback = "booking_status_callback";
    public const string BookingStatusReconcile = "booking_status_reconcile";
    public const string BookingStatusRead = "booking_status_read";
}

public static class InternalServiceProblemCodes
{
    public const string HttpsRequired = "https_required";
    public const string MissingAuthenticationHeader = "internal_auth_missing_header";
    public const string InvalidServiceId = "internal_auth_invalid_service";
    public const string InvalidTimestamp = "internal_auth_invalid_timestamp";
    public const string TimestampOutOfRange = "internal_auth_timestamp_out_of_range";
    public const string InvalidNonce = "internal_auth_invalid_nonce";
    public const string ReplayNonce = "internal_auth_replay_nonce";
    public const string InvalidSignature = "internal_auth_invalid_signature";
    public const string RequestBodyTooLarge = "internal_request_body_too_large";
    public const string ServiceForbidden = "internal_service_forbidden";
    public const string IdempotencyKeyRequired = "idempotency_key_required";
    public const string IdempotencyKeyInvalid = "idempotency_key_invalid";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string IdempotencyUnavailable = "idempotency_unavailable";
    public const string ContractInvalid = "internal_contract_invalid";
}

public static class InternalServiceHeaderValueValidator
{
    private static readonly Regex SafeTokenPattern = new(
        @"^[A-Za-z0-9._:-]{1,128}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NoncePattern = new(
        @"^[A-Za-z0-9_-]{32,128}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValidServiceId(string? value)
    {
        return IsSafeToken(value, InternalServiceWireConstants.MaxServiceIdLength);
    }

    public static bool IsValidCorrelationId(string? value)
    {
        return IsSafeToken(value, InternalServiceWireConstants.MaxCorrelationIdLength);
    }

    public static bool IsValidIdempotencyKey(string? value)
    {
        return IsSafeToken(value, InternalServiceWireConstants.MaxIdempotencyKeyLength);
    }

    public static bool IsValidNonce(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= InternalServiceWireConstants.MaxNonceLength
            && NoncePattern.IsMatch(value);
    }

    public static string GetOrCreateCorrelationId(string? suppliedValue)
    {
        return IsValidCorrelationId(suppliedValue)
            ? suppliedValue!
            : ActivityTraceId.CreateRandom().ToString();
    }

    private static bool IsSafeToken(string? value, int maxLength)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= maxLength
            && SafeTokenPattern.IsMatch(value);
    }
}

public static class InternalServiceCanonicalRequest
{
    public static string Build(
        string serviceId,
        string method,
        string path,
        IEnumerable<KeyValuePair<string, string?>> queryParameters,
        string timestamp,
        string nonce,
        string bodyHashHex)
    {
        return Build(
            serviceId,
            method,
            path,
            queryParameters,
            timestamp,
            nonce,
            idempotencyKey: null,
            bodyHashHex);
    }

    public static string Build(
        string serviceId,
        string method,
        string path,
        IEnumerable<KeyValuePair<string, string?>> queryParameters,
        string timestamp,
        string nonce,
        string? idempotencyKey,
        string bodyHashHex)
    {
        return string.Join('\n',
            InternalServiceWireConstants.SignatureVersion,
            serviceId,
            method.ToUpperInvariant(),
            NormalizePathAndQuery(path, queryParameters),
            timestamp,
            nonce,
            idempotencyKey ?? string.Empty,
            bodyHashHex);
    }

    public static string NormalizePathAndQuery(
        string path,
        IEnumerable<KeyValuePair<string, string?>> queryParameters)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? "/"
            : path;

        var normalizedPairs = queryParameters
            .Select(pair => new KeyValuePair<string, string?>(
                pair.Key ?? string.Empty,
                pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value ?? string.Empty, StringComparer.Ordinal)
            .Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}")
            .ToArray();

        return normalizedPairs.Length == 0
            ? normalizedPath
            : $"{normalizedPath}?{string.Join("&", normalizedPairs)}";
    }

    public static string ComputeSha256Hex(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

public sealed class InternalServiceProblemResponse
{
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Status { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public IReadOnlyCollection<string> MissingHeaders { get; set; } = Array.Empty<string>();
}
