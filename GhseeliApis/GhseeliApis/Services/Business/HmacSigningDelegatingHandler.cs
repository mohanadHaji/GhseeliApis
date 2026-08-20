using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace GhseeliApis.Services.Business;

public sealed class HmacSigningDelegatingHandler : DelegatingHandler
{
    private readonly IOptions<BusinessApiClientOptions> _options;

    public HmacSigningDelegatingHandler(IOptions<BusinessApiClientOptions> options)
    {
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!InternalServiceHeaderValueValidator.IsValidServiceId(options.ServiceId))
        {
            throw new InvalidOperationException("BusinessApiClient:ServiceId is invalid.");
        }

        if (string.IsNullOrWhiteSpace(options.ActiveSecret) || options.ActiveSecret.Length < 32)
        {
            throw new InvalidOperationException("BusinessApiClient:ActiveSecret is invalid.");
        }

        var bodyBytes = request.Content is null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var bodyHash = InternalServiceCanonicalRequest.ComputeSha256Hex(bodyBytes);
        var timestamp = DateTime.UtcNow.ToString("O");
        var nonce = CreateNonce();
        var queryParameters = ParseQueryParameters(request.RequestUri);
        var idempotencyKey = request.Headers.TryGetValues(
            InternalServiceWireConstants.IdempotencyKeyHeaderName,
            out var idempotencyKeyValues)
            ? string.Join(",", idempotencyKeyValues)
            : string.Empty;
        var path = request.RequestUri?.IsAbsoluteUri == true
            ? request.RequestUri.AbsolutePath
            : request.RequestUri?.OriginalString.Split('?', 2)[0] ?? "/";
        var canonicalRequest = InternalServiceCanonicalRequest.Build(
            options.ServiceId,
            request.Method.Method,
            path,
            queryParameters,
            timestamp,
            nonce,
            idempotencyKey,
            bodyHash);
        var signature = ComputeSignature(options.ActiveSecret, canonicalRequest);

        request.Headers.Remove(InternalServiceWireConstants.ServiceIdHeaderName);
        request.Headers.Remove(InternalServiceWireConstants.TimestampHeaderName);
        request.Headers.Remove(InternalServiceWireConstants.NonceHeaderName);
        request.Headers.Remove(InternalServiceWireConstants.SignatureHeaderName);

        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.ServiceIdHeaderName,
            options.ServiceId);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.TimestampHeaderName,
            timestamp);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.NonceHeaderName,
            nonce);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.SignatureHeaderName,
            signature);

        return await base.SendAsync(request, cancellationToken);
    }

    private static IEnumerable<KeyValuePair<string, string?>> ParseQueryParameters(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return Array.Empty<KeyValuePair<string, string?>>();
        }

        var query = requestUri.IsAbsoluteUri
            ? requestUri.Query
            : new Uri("https://placeholder" + requestUri.OriginalString).Query;

        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query)
            .SelectMany(pair => pair.Value.Count == 0
                ? [new KeyValuePair<string, string?>(pair.Key, string.Empty)]
                : pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)));
    }

    private static string CreateNonce()
    {
        return Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
    }

    private static string ComputeSignature(string secret, string canonicalRequest)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalRequest)))
            .ToLowerInvariant();
    }
}
