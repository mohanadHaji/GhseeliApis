using Ghseeli.IntegrationContracts.InternalHttp;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

internal static class InternalServiceTestRequestFactory
{
    private static readonly JsonSerializerOptions JsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    public static async Task<HttpRequestMessage> CreateSignedRequestAsync(
        HttpClient client,
        HttpMethod method,
        string relativeUri,
        object? body = null,
        string? idempotencyKey = null,
        string? correlationId = null,
        string? nonce = null,
        DateTimeOffset? timestamp = null,
        string? serviceId = null,
        string? secret = null)
    {
        var request = new HttpRequestMessage(method, relativeUri);
        var bodyBytes = Array.Empty<byte>();

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            bodyBytes = Encoding.UTF8.GetBytes(json);
            request.Content = new ByteArrayContent(bodyBytes);
            request.Content.Headers.ContentType = new("application/json");
        }

        var effectiveServiceId = serviceId ?? CatalogApiFactory.InternalServiceId;
        var effectiveSecret = secret ?? CatalogApiFactory.InternalServiceActiveSecret;
        var effectiveTimestamp = timestamp ?? DateTimeOffset.UtcNow;
        var effectiveNonce = nonce ?? Guid.NewGuid().ToString("N");
        var generatedCorrelationId = $"corr-{Guid.NewGuid():N}";
        var effectiveCorrelationId = correlationId ??
            generatedCorrelationId[..Math.Min(
                generatedCorrelationId.Length,
                InternalServiceWireConstants.MaxCorrelationIdLength)];
        var absoluteUri = new Uri(client.BaseAddress!, relativeUri);
        var queryParameters = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(absoluteUri.Query)
            .SelectMany(pair => pair.Value.Count == 0
                ? [new KeyValuePair<string, string?>(pair.Key, string.Empty)]
                : pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)));
        var canonicalRequest = InternalServiceCanonicalRequest.Build(
            effectiveServiceId,
            method.Method,
            absoluteUri.AbsolutePath,
            queryParameters,
            effectiveTimestamp.UtcDateTime.ToString("O"),
            effectiveNonce,
            idempotencyKey,
            InternalServiceCanonicalRequest.ComputeSha256Hex(bodyBytes));
        var signature = ComputeSignature(effectiveSecret, canonicalRequest);

        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.ServiceIdHeaderName,
            effectiveServiceId);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.TimestampHeaderName,
            effectiveTimestamp.UtcDateTime.ToString("O"));
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.NonceHeaderName,
            effectiveNonce);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.SignatureHeaderName,
            signature);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.CorrelationIdHeaderName,
            effectiveCorrelationId);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation(
                InternalServiceWireConstants.IdempotencyKeyHeaderName,
                idempotencyKey);
        }

        await Task.CompletedTask;
        return request;
    }

    private static string ComputeSignature(string secret, string canonicalRequest)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalRequest)))
            .ToLowerInvariant();
    }
}
