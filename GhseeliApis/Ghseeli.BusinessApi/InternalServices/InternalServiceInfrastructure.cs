using Ghseeli.BusinessApi.Constants;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ghseeli.BusinessApi.InternalServices;

internal static class InternalServiceHttpContextKeys
{
    public const string AuthenticationFailure = "Ghseeli.Internal.AuthenticationFailure";
    public const string RequestBodyBytes = "Ghseeli.Internal.RequestBodyBytes";
    public const string RequestBodyHash = "Ghseeli.Internal.RequestBodyHash";
    public const string CorrelationId = "Ghseeli.Internal.CorrelationId";
}

public sealed class InternalServiceAuthenticationFailure
{
    private InternalServiceAuthenticationFailure(
        string code,
        int statusCode,
        string title,
        string detail,
        IReadOnlyCollection<string>? missingHeaders = null)
    {
        Code = code;
        StatusCode = statusCode;
        Title = title;
        Detail = detail;
        MissingHeaders = missingHeaders ?? Array.Empty<string>();
    }

    public string Code { get; }
    public int StatusCode { get; }
    public string Title { get; }
    public string Detail { get; }
    public IReadOnlyCollection<string> MissingHeaders { get; }

    public static InternalServiceAuthenticationFailure HttpsRequired() =>
        new(
            InternalServiceProblemCodes.HttpsRequired,
            StatusCodes.Status403Forbidden,
            "HTTPS is required.",
            "Internal service requests must use HTTPS.");

    public static InternalServiceAuthenticationFailure ForMissingHeaders(
        IReadOnlyCollection<string> missingHeaders) =>
        new(
            InternalServiceProblemCodes.MissingAuthenticationHeader,
            StatusCodes.Status401Unauthorized,
            "Internal authentication headers are missing.",
            "One or more required internal authentication headers were not supplied.",
            missingHeaders);

    public static InternalServiceAuthenticationFailure InvalidServiceId() =>
        new(
            InternalServiceProblemCodes.InvalidServiceId,
            StatusCodes.Status401Unauthorized,
            "Internal service request was rejected.",
            "The supplied internal service identifier is invalid or unknown.");

    public static InternalServiceAuthenticationFailure InvalidTimestamp(string detail) =>
        new(
            InternalServiceProblemCodes.InvalidTimestamp,
            StatusCodes.Status401Unauthorized,
            "Internal service request was rejected.",
            detail);

    public static InternalServiceAuthenticationFailure TimestampOutOfRange() =>
        new(
            InternalServiceProblemCodes.TimestampOutOfRange,
            StatusCodes.Status401Unauthorized,
            "Internal service request was rejected.",
            "The supplied timestamp is outside the allowed clock-skew window.");

    public static InternalServiceAuthenticationFailure InvalidNonce() =>
        new(
            InternalServiceProblemCodes.InvalidNonce,
            StatusCodes.Status401Unauthorized,
            "Internal service request was rejected.",
            "The supplied nonce is invalid.");

    public static InternalServiceAuthenticationFailure ReplayNonce() =>
        new(
            InternalServiceProblemCodes.ReplayNonce,
            StatusCodes.Status401Unauthorized,
            "Internal service request was rejected.",
            "The supplied nonce has already been used within the replay window.");

    public static InternalServiceAuthenticationFailure InvalidSignature() =>
        new(
            InternalServiceProblemCodes.InvalidSignature,
            StatusCodes.Status401Unauthorized,
            "Internal service request was rejected.",
            "The supplied signature is invalid.");

    public static InternalServiceAuthenticationFailure RequestBodyTooLarge() =>
        new(
            InternalServiceProblemCodes.RequestBodyTooLarge,
            StatusCodes.Status413PayloadTooLarge,
            "Internal service request was rejected.",
            "The request body exceeds the allowed size.");
}

public sealed class InternalServiceAuthenticationResult
{
    private InternalServiceAuthenticationResult(
        InternalServiceDefinition? service,
        InternalServiceAuthenticationFailure? failure)
    {
        Service = service;
        Failure = failure;
    }

    public InternalServiceDefinition? Service { get; }
    public InternalServiceAuthenticationFailure? Failure { get; }
    public bool IsAuthenticated => Service is not null && Failure is null;

    public static InternalServiceAuthenticationResult Success(InternalServiceDefinition service)
        => new(service, null);

    public static InternalServiceAuthenticationResult Rejected(
        InternalServiceAuthenticationFailure failure)
        => new(null, failure);
}

public static class InternalServiceProblemResponseFactory
{
    private static readonly JsonSerializerOptions JsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        string code,
        IReadOnlyCollection<string>? missingHeaders = null)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var response = new Dictionary<string, object?>
        {
            ["type"] = $"https://api.ghseeli.example/errors/{code}",
            ["title"] = title,
            ["status"] = statusCode,
            ["detail"] = detail,
            ["code"] = code,
            ["correlationId"] = context.GetCorrelationId()
        };
        if (missingHeaders?.Count > 0)
        {
            response["missingHeaders"] = missingHeaders;
        }

        return context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    public static Task WriteAsync(
        HttpContext context,
        InternalServiceAuthenticationFailure failure)
    {
        return WriteAsync(
            context,
            failure.StatusCode,
            failure.Title,
            failure.Detail,
            failure.Code,
            failure.MissingHeaders);
    }
}

public static class BusinessAuthenticationProblemResponseFactory
{
    private static readonly JsonSerializerOptions JsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        string code)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-store";
        if (statusCode == StatusCodes.Status401Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }

        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = $"https://api.ghseeli.example/errors/{code}",
            title,
            status = statusCode,
            detail,
            code,
            correlationId = context.GetCorrelationId()
        }, JsonOptions));
    }
}

public static class InternalServiceHttpContextExtensions
{
    public static string GetCorrelationId(this HttpContext context)
    {
        return context.Items.TryGetValue(InternalServiceHttpContextKeys.CorrelationId, out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;
    }
}

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[
            InternalServiceWireConstants.CorrelationIdHeaderName].ToString();
        var correlationId = InternalServiceHeaderValueValidator.GetOrCreateCorrelationId(supplied);
        if (string.IsNullOrWhiteSpace(supplied) &&
            context.Request.Headers.Authorization.Count == 1)
        {
            correlationId = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString())))
                .ToLowerInvariant()[..32];
        }

        context.Request.Headers[InternalServiceWireConstants.CorrelationIdHeaderName] = correlationId;
        context.Items[InternalServiceHttpContextKeys.CorrelationId] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[InternalServiceWireConstants.CorrelationIdHeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}

public static class InternalServiceRequestBodyAccessor
{
    public static async Task<byte[]> GetBodyBytesAsync(
        HttpContext context,
        InternalServiceAuthenticationOptions options,
        CancellationToken cancellationToken)
    {
        if (context.Items.TryGetValue(InternalServiceHttpContextKeys.RequestBodyBytes, out var cachedBytes) &&
            cachedBytes is byte[] bodyBytes)
        {
            return bodyBytes;
        }

        if (context.Request.ContentLength.HasValue &&
            context.Request.ContentLength.Value > options.MaxRequestBodyBytes)
        {
            throw new InternalRequestBodyTooLargeException();
        }

        var bufferThreshold = options.MaxRequestBodyBytes == int.MaxValue
            ? int.MaxValue
            : options.MaxRequestBodyBytes + 1;
        context.Request.EnableBuffering(bufferThreshold);

        try
        {
            context.Request.Body.Position = 0;
            var bufferedBytes = await ReadBoundedBodyAsync(
                context.Request.Body,
                options.MaxRequestBodyBytes,
                cancellationToken);

            context.Request.Body.Position = 0;
            context.Items[InternalServiceHttpContextKeys.RequestBodyBytes] = bufferedBytes;
            context.Items[InternalServiceHttpContextKeys.RequestBodyHash] =
                InternalServiceCanonicalRequest.ComputeSha256Hex(bufferedBytes);

            return bufferedBytes;
        }
        catch
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }

            throw;
        }
    }

    public static async Task<string> GetBodyHashAsync(
        HttpContext context,
        InternalServiceAuthenticationOptions options,
        CancellationToken cancellationToken)
    {
        if (context.Items.TryGetValue(InternalServiceHttpContextKeys.RequestBodyHash, out var cachedHash) &&
            cachedHash is string bodyHash)
        {
            return bodyHash;
        }

        _ = await GetBodyBytesAsync(context, options, cancellationToken);
        return (string)context.Items[InternalServiceHttpContextKeys.RequestBodyHash]!;
    }

    internal static async Task<byte[]> ReadBoundedBodyAsync(
        Stream requestBody,
        int maxRequestBodyBytes,
        CancellationToken cancellationToken)
    {
        var maxBufferedBytes = maxRequestBodyBytes == int.MaxValue
            ? int.MaxValue
            : maxRequestBodyBytes + 1;
        var readBuffer = ArrayPool<byte>.Shared.Rent(Math.Min(81920, maxBufferedBytes));

        try
        {
            using var memoryStream = new MemoryStream(Math.Min(maxBufferedBytes, 4096));

            while (true)
            {
                var remaining = maxBufferedBytes - (int)memoryStream.Length;
                if (remaining <= 0)
                {
                    throw new InternalRequestBodyTooLargeException();
                }

                var bytesRead = await requestBody.ReadAsync(
                    readBuffer.AsMemory(0, Math.Min(readBuffer.Length, remaining)),
                    cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await memoryStream.WriteAsync(readBuffer.AsMemory(0, bytesRead), cancellationToken);
                if (memoryStream.Length > maxRequestBodyBytes)
                {
                    throw new InternalRequestBodyTooLargeException();
                }
            }

            return memoryStream.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
        }
    }
}

public sealed class InternalRequestBodyTooLargeException : Exception;

public sealed class InternalServiceRequestValidator
{
    private readonly IInternalServiceNonceStore _nonceStore;
    private readonly Services.Availability.ISystemClock _clock;
    private readonly IOptions<InternalServiceAuthenticationOptions> _options;
    private readonly IAppLogger _logger;

    public InternalServiceRequestValidator(
        IInternalServiceNonceStore nonceStore,
        Services.Availability.ISystemClock clock,
        IOptions<InternalServiceAuthenticationOptions> options,
        IAppLogger logger)
    {
        _nonceStore = nonceStore;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    public async Task<InternalServiceAuthenticationResult> ValidateAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var correlationId = context.GetCorrelationId();

        if (options.RequireHttps &&
            !(options.AllowInsecureHttpInDevelopment &&
              context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()) &&
            !context.Request.IsHttps)
        {
            return Reject(correlationId, InternalServiceAuthenticationFailure.HttpsRequired());
        }

        var requiredHeaders = new[]
        {
            InternalServiceWireConstants.ServiceIdHeaderName,
            InternalServiceWireConstants.TimestampHeaderName,
            InternalServiceWireConstants.NonceHeaderName,
            InternalServiceWireConstants.SignatureHeaderName
        };
        var missingHeaders = requiredHeaders
            .Select((header, index) => new { header, index })
            .Where(item =>
                !context.Request.Headers.TryGetValue(item.header, out var value) ||
                string.IsNullOrWhiteSpace(value.ToString()))
            .Select(item => item.index switch
            {
                0 => "serviceId",
                1 => "timestamp",
                2 => "nonce",
                _ => "signature"
            })
            .ToArray();
        if (missingHeaders.Length > 0)
        {
            return Reject(correlationId, InternalServiceAuthenticationFailure.ForMissingHeaders(missingHeaders));
        }

        if (requiredHeaders.Any(header =>
                context.Request.Headers[header].Count != 1 ||
                context.Request.Headers[header].ToString().Contains(',', StringComparison.Ordinal)))
        {
            return Reject(correlationId, InternalServiceAuthenticationFailure.InvalidSignature());
        }

        var serviceId = context.Request.Headers[InternalServiceWireConstants.ServiceIdHeaderName].ToString();
        if (!InternalServiceHeaderValueValidator.IsValidServiceId(serviceId))
        {
            return Reject(correlationId, InternalServiceAuthenticationFailure.InvalidServiceId());
        }

        var service = options.Services.SingleOrDefault(candidate =>
            string.Equals(candidate.ServiceId, serviceId, StringComparison.Ordinal));
        if (service is null)
        {
            return Reject(correlationId, InternalServiceAuthenticationFailure.InvalidServiceId());
        }

        var timestampValue = context.Request.Headers[InternalServiceWireConstants.TimestampHeaderName].ToString();
        if (!DateTimeOffset.TryParseExact(
                timestampValue,
                "O",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            return Reject(
                correlationId,
                InternalServiceAuthenticationFailure.InvalidTimestamp(
                    "The supplied timestamp is not a valid UTC ISO 8601 value."));
        }

        var utcNow = new DateTimeOffset(_clock.UtcNow, TimeSpan.Zero);
        var skew = TimeSpan.FromSeconds(options.AllowedClockSkewSeconds);
        if (timestamp < utcNow - skew || timestamp > utcNow + skew)
        {
            return Reject(correlationId, InternalServiceAuthenticationFailure.TimestampOutOfRange());
        }

        var nonce = context.Request.Headers[InternalServiceWireConstants.NonceHeaderName].ToString();
        if (!InternalServiceHeaderValueValidator.IsValidNonce(nonce))
        {
            return Reject(correlationId, InternalServiceAuthenticationFailure.InvalidNonce());
        }

        string bodyHash;
        try
        {
            bodyHash = await InternalServiceRequestBodyAccessor.GetBodyHashAsync(
                context,
                options,
                cancellationToken);
        }
        catch (InternalRequestBodyTooLargeException)
        {
            return Reject(correlationId, InternalServiceAuthenticationFailure.RequestBodyTooLarge());
        }

        var queryParameters = context.Request.Query
            .SelectMany(pair => pair.Value.Count == 0
                ? [new KeyValuePair<string, string?>(pair.Key, string.Empty)]
                : pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)))
            .ToArray();
        var canonicalRequest = InternalServiceCanonicalRequest.Build(
            serviceId,
            context.Request.Method,
            context.Request.Path.ToUriComponent(),
            queryParameters,
            timestamp.UtcDateTime.ToString("O"),
            nonce,
            context.Request.Headers[InternalServiceWireConstants.IdempotencyKeyHeaderName].ToString(),
            bodyHash);
        var providedSignature = context.Request.Headers[InternalServiceWireConstants.SignatureHeaderName]
            .ToString();

        if (!MatchesAnySignature(service, canonicalRequest, providedSignature))
        {
            return Reject(correlationId, InternalServiceAuthenticationFailure.InvalidSignature());
        }

        var nonceAccepted = await _nonceStore.TryAcceptAsync(
            serviceId,
            nonce,
            _clock.UtcNow,
            _clock.UtcNow.AddSeconds(options.NonceLifetimeSeconds),
            cancellationToken);
        if (!nonceAccepted)
        {
            return Reject(correlationId, InternalServiceAuthenticationFailure.ReplayNonce());
        }

        return InternalServiceAuthenticationResult.Success(service);
    }

    private InternalServiceAuthenticationResult Reject(
        string correlationId,
        InternalServiceAuthenticationFailure failure)
    {
        _logger.LogWarning(
            $"Internal service authentication rejected. CorrelationId={correlationId}, Code={failure.Code}.");
        return InternalServiceAuthenticationResult.Rejected(failure);
    }

    private static bool MatchesAnySignature(
        InternalServiceDefinition service,
        string canonicalRequest,
        string providedSignature)
    {
        if (string.IsNullOrWhiteSpace(providedSignature) ||
            providedSignature.Length != 64 ||
            !IsHex(providedSignature))
        {
            return false;
        }

        var providedBytes = Convert.FromHexString(providedSignature);

        return MatchesSecret(service.ActiveSecret) ||
               (!string.IsNullOrWhiteSpace(service.NextSecret) && MatchesSecret(service.NextSecret!));

        bool MatchesSecret(string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalRequest));
            return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }
    }

    private static bool IsHex(string value)
    {
        return value.All(character =>
            char.IsAsciiHexDigit(character));
    }
}
