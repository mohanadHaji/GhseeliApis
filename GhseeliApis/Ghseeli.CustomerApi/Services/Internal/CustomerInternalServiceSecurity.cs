using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GhseeliApis.Services.Internal;

[AttributeUsage(AttributeTargets.Method)]
public sealed class CustomerInternalOperationAttribute : Attribute
{
    public CustomerInternalOperationAttribute(string operation) => Operation = operation;
    public string Operation { get; }
}

public sealed class CustomerInternalServiceOptions
{
    public const string SectionName = "CustomerInternalServiceAuthentication";
    public bool RequireHttps { get; set; } = true;
    public bool AllowInsecureHttpInDevelopment { get; set; }
    public int AllowedClockSkewSeconds { get; set; } = 120;
    public int NonceLifetimeSeconds { get; set; } = 300;
    public int IdempotencyLifetimeSeconds { get; set; } = 86_400;
    public int InProgressRecoverySeconds { get; set; } = 30;
    public double InProgressLeaseRenewalFraction { get; set; } = 0.333333;
    public int InProgressLeaseSafetyMarginMilliseconds { get; set; } = 250;
    public int InProgressLeaseOperationTimeoutMilliseconds { get; set; } = 2_000;
    public int InProgressLeaseRetryBackoffMilliseconds { get; set; } = 50;
    public int InProgressWaitMilliseconds { get; set; } = 2_000;
    public int InProgressPollMilliseconds { get; set; } = 25;
    public int MaxRequestBodyBytes { get; set; } = 65_536;
    public int MaxStoredResponseBytes { get; set; } = 65_536;
    public int ExpiredRecordCleanupBatchSize { get; set; } = 100;
    public List<CustomerInternalServiceDefinition> Services { get; set; } = [];
}

public sealed class CustomerInternalServiceDefinition
{
    public string ServiceId { get; set; } = string.Empty;
    public string ActiveSecret { get; set; } = string.Empty;
    public string? NextSecret { get; set; }
    public List<string> AllowedOperations { get; set; } = [];
}

public sealed class CustomerInternalServiceMiddleware
{
    internal const string RequestHashKey = "Ghseeli.CustomerInternal.RequestHash";
    private readonly RequestDelegate _next;

    public CustomerInternalServiceMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<CustomerInternalServiceOptions> optionsAccessor,
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        IWebHostEnvironment environment,
        IAppLogger logger,
        ICustomerInternalIdempotencyCleanupService cleanupService,
        ICustomerInternalIdempotencyLeaseService leaseService,
        IServiceScopeFactory scopeFactory)
    {
        var operation = context.GetEndpoint()?
            .Metadata.GetMetadata<CustomerInternalOperationAttribute>()?.Operation;
        if (operation is null)
        {
            await _next(context);
            return;
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });

        var options = optionsAccessor.Value;
        if (options.RequireHttps &&
            !(environment.IsDevelopment() && options.AllowInsecureHttpInDevelopment) &&
            !context.Request.IsHttps)
        {
            await RejectAsync(context, 403, InternalServiceProblemCodes.HttpsRequired, operation);
            return;
        }

        var serviceId = Header(context, InternalServiceWireConstants.ServiceIdHeaderName);
        var timestampText = Header(context, InternalServiceWireConstants.TimestampHeaderName);
        var nonce = Header(context, InternalServiceWireConstants.NonceHeaderName);
        var signature = Header(context, InternalServiceWireConstants.SignatureHeaderName);
        var idempotencyKey = Header(context, InternalServiceWireConstants.IdempotencyKeyHeaderName);
        if (string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrWhiteSpace(timestampText) ||
            string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(signature))
        {
            await RejectAsync(
                context, 401, InternalServiceProblemCodes.MissingAuthenticationHeader, operation);
            return;
        }

        var service = options.Services.SingleOrDefault(value =>
            string.Equals(value.ServiceId, serviceId, StringComparison.Ordinal));
        if (service is null || !InternalServiceHeaderValueValidator.IsValidServiceId(serviceId))
        {
            await RejectAsync(context, 401, InternalServiceProblemCodes.InvalidServiceId, operation);
            return;
        }
        if (!service.AllowedOperations.Contains(operation, StringComparer.Ordinal))
        {
            await RejectAsync(context, 403, InternalServiceProblemCodes.ServiceForbidden, operation);
            return;
        }
        if (HttpMethods.IsPost(context.Request.Method) &&
            !InternalServiceHeaderValueValidator.IsValidIdempotencyKey(idempotencyKey))
        {
            await RejectAsync(
                context,
                400,
                string.IsNullOrWhiteSpace(idempotencyKey)
                    ? InternalServiceProblemCodes.IdempotencyKeyRequired
                    : InternalServiceProblemCodes.IdempotencyKeyInvalid,
                operation);
            return;
        }
        if (!DateTimeOffset.TryParseExact(
                timestampText,
                "O",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var timestamp))
        {
            await RejectAsync(context, 401, InternalServiceProblemCodes.InvalidTimestamp, operation);
            return;
        }

        var now = timeProvider.GetUtcNow();
        if ((now - timestamp.ToUniversalTime()).Duration() >
            TimeSpan.FromSeconds(options.AllowedClockSkewSeconds))
        {
            await RejectAsync(
                context, 401, InternalServiceProblemCodes.TimestampOutOfRange, operation);
            return;
        }
        if (!InternalServiceHeaderValueValidator.IsValidNonce(nonce))
        {
            await RejectAsync(context, 401, InternalServiceProblemCodes.InvalidNonce, operation);
            return;
        }

        byte[] body;
        try
        {
            body = await ReadBodyAsync(context, options.MaxRequestBodyBytes);
        }
        catch (InvalidDataException)
        {
            await RejectAsync(
                context,
                413,
                operation == InternalServiceOperationNames.BookingStatusCallback
                    ? BookingStatusErrorCodes.RequestBodyTooLarge
                    : InternalServiceProblemCodes.RequestBodyTooLarge,
                operation);
            return;
        }

        var rawBodyHash = InternalServiceCanonicalRequest.ComputeSha256Hex(body);
        var query = context.Request.Query.SelectMany(pair => pair.Value.Count == 0
            ? [new KeyValuePair<string, string?>(pair.Key, string.Empty)]
            : pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)));
        var queryPairs = query.ToArray();
        var canonical = InternalServiceCanonicalRequest.Build(
            serviceId,
            context.Request.Method,
            context.Request.Path,
            queryPairs,
            timestampText,
            nonce,
            idempotencyKey,
            rawBodyHash);
        if (!ValidSignature(signature, service.ActiveSecret, canonical) &&
            !ValidSignature(signature, service.NextSecret, canonical))
        {
            await RejectAsync(context, 401, InternalServiceProblemCodes.InvalidSignature, operation);
            return;
        }

        if (!await TryAcceptNonceAsync(
                dbContext, serviceId, nonce, now, options, context.RequestAborted))
        {
            logger.LogWarning(
                $"Rejected replayed internal nonce. ServiceId={serviceId}, Operation={operation}, CorrelationId={context.TraceIdentifier}.");
            await RejectAsync(context, 401, InternalServiceProblemCodes.ReplayNonce, operation);
            return;
        }

        context.Items[RequestHashKey] =
            InternalServiceCanonicalRequest.ComputeSha256Hex(CanonicalizeBody(body));
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await _next(context);
            return;
        }

        try
        {
            await cleanupService.CleanupExpiredAsync(context.RequestAborted);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogError(
                "Customer internal idempotency retention cleanup failed.",
                exception);
        }

        var transportHash = ComputeTransportHash(
            context.Request.Method,
            context.Request.Path,
            queryPairs,
            context.Request.ContentType,
            body);
        var claim = await leaseService.ClaimAsync(
            serviceId,
            operation,
            idempotencyKey,
            transportHash,
            context.RequestAborted);

        if (claim.Kind == CustomerInternalIdempotencyClaimKind.Conflict)
        {
            await RejectAsync(
                context, 409, InternalServiceProblemCodes.IdempotencyConflict, operation);
            return;
        }
        if (claim.Kind == CustomerInternalIdempotencyClaimKind.Replay)
        {
            await ReplayAsync(context, claim.Record!);
            return;
        }
        if (claim.Kind == CustomerInternalIdempotencyClaimKind.InProgress)
        {
            var completed = await WaitForCompletionAsync(
                dbContext,
                serviceId,
                operation,
                idempotencyKey,
                transportHash,
                options,
                timeProvider,
                context.RequestAborted);
            if (completed is not null)
            {
                await ReplayAsync(context, completed);
                return;
            }
            await RejectAsync(
                context, 503, InternalServiceProblemCodes.IdempotencyUnavailable, operation);
            return;
        }

        await ExecuteAndCompleteAsync(
            context,
            scopeFactory,
            claim.RecordId,
            claim.OwnerToken,
            claim.Record!.LeaseExpiresAtUtc!.Value,
            options,
            timeProvider,
            operation,
            logger);
    }

    internal static async Task<byte[]> ReadBodyAsync(HttpContext context, int maximum)
    {
        if (context.Request.ContentLength > maximum)
        {
            throw new InvalidDataException();
        }

        context.Request.EnableBuffering();
        var buffer = new byte[maximum + 1];
        var total = 0;
        while (total <= maximum)
        {
            var read = await context.Request.Body.ReadAsync(
                buffer.AsMemory(total, maximum + 1 - total),
                context.RequestAborted);
            if (read == 0)
            {
                context.Request.Body.Position = 0;
                return buffer[..total];
            }
            total += read;
        }

        context.Request.Body.Position = 0;
        throw new InvalidDataException();
    }

    private async Task ExecuteAndCompleteAsync(
        HttpContext context,
        IServiceScopeFactory scopeFactory,
        Guid recordId,
        Guid ownerToken,
        DateTimeOffset initialLeaseExpiryUtc,
        CustomerInternalServiceOptions options,
        TimeProvider timeProvider,
        string operation,
        IAppLogger logger)
    {
        var originalBody = context.Response.Body;
        var originalRequestAborted = context.RequestAborted;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        await using var heartbeat = CustomerInternalLeaseHeartbeat.Start(
            scopeFactory,
            recordId,
            ownerToken,
            initialLeaseExpiryUtc,
            options,
            timeProvider,
            logger);
        context.RequestAborted = heartbeat.LeaseLostToken;
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException)
        {
            await heartbeat.StopAsync();
            context.RequestAborted = originalRequestAborted;
            context.Response.Body = originalBody;
            throw;
        }
        catch
        {
            await heartbeat.StopAsync();
            context.RequestAborted = originalRequestAborted;
            context.Response.Body = originalBody;
            throw;
        }

        if (buffer.Length > options.MaxStoredResponseBytes)
        {
            await heartbeat.StopAsync();
            context.RequestAborted = originalRequestAborted;
            context.Response.Body = originalBody;
            context.Response.Clear();
            await RejectAsync(
                context, 503, InternalServiceProblemCodes.IdempotencyUnavailable, operation);
            return;
        }

        try
        {
            await heartbeat.StopAsync();
            if (heartbeat.LeaseLostToken.IsCancellationRequested)
            {
                context.RequestAborted = originalRequestAborted;
                context.Response.Body = originalBody;
                context.Response.Clear();
                await RejectAsync(
                    context, 503, InternalServiceProblemCodes.IdempotencyUnavailable, operation);
                return;
            }
            var completed = await CompleteWithRetryAsync(
                scopeFactory,
                recordId,
                ownerToken,
                context.Response.StatusCode,
                context.Response.ContentType ?? "application/json",
                buffer.ToArray(),
                heartbeat.LastConfirmedExpiryUtc,
                options,
                timeProvider,
                logger);
            if (!completed)
            {
                context.RequestAborted = originalRequestAborted;
                context.Response.Body = originalBody;
                context.Response.Clear();
                await RejectAsync(
                    context, 503, InternalServiceProblemCodes.IdempotencyUnavailable, operation);
                return;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Failed to complete a Customer internal idempotency record.",
                exception);
            context.RequestAborted = originalRequestAborted;
            context.Response.Body = originalBody;
            context.Response.Clear();
            await RejectAsync(
                context, 503, InternalServiceProblemCodes.IdempotencyUnavailable, operation);
            return;
        }

        context.RequestAborted = originalRequestAborted;
        context.Response.Body = originalBody;
        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, originalRequestAborted);
    }

    private static async Task<bool> TryAcceptNonceAsync(
        ApplicationDbContext context,
        string serviceId,
        string nonce,
        DateTimeOffset now,
        CustomerInternalServiceOptions options,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var strategy = context.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    context.ChangeTracker.Clear();
                    await using var transaction = context.Database.IsRelational()
                        ? await context.Database.BeginTransactionAsync(
                            IsolationLevel.Serializable, cancellationToken)
                        : null;
                    var nonceQuery = context.Database.IsRelational()
                        ? context.CustomerInternalServiceNonces.FromSqlInterpolated(
                            $"SELECT * FROM [CustomerInternalServiceNonces] WITH (UPDLOCK, HOLDLOCK) WHERE [ServiceId] = {serviceId} AND [Nonce] = {nonce}")
                        : context.CustomerInternalServiceNonces.Where(
                            value => value.ServiceId == serviceId && value.Nonce == nonce);
                    var existing = await nonceQuery.SingleOrDefaultAsync(cancellationToken);
                    if (existing is not null && existing.ExpiresAtUtc > now)
                    {
                        if (transaction is not null)
                            await transaction.RollbackAsync(cancellationToken);
                        return false;
                    }

                    if (existing is null)
                    {
                        context.CustomerInternalServiceNonces.Add(new CustomerInternalServiceNonce
                        {
                            ServiceId = serviceId,
                            Nonce = nonce,
                            AcceptedAtUtc = now,
                            ExpiresAtUtc = now.AddSeconds(options.NonceLifetimeSeconds)
                        });
                    }
                    else
                    {
                        existing.AcceptedAtUtc = now;
                        existing.ExpiresAtUtc = now.AddSeconds(options.NonceLifetimeSeconds);
                    }

                    if (context.Database.IsRelational())
                    {
                        await context.CustomerInternalServiceNonces
                            .Where(value =>
                                value.ExpiresAtUtc <= now &&
                                !(value.ServiceId == serviceId && value.Nonce == nonce))
                            .ExecuteDeleteAsync(cancellationToken);
                    }
                    await context.SaveChangesAsync(cancellationToken);
                    if (transaction is not null)
                        await transaction.CommitAsync(cancellationToken);
                    return true;
                });
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                context.ChangeTracker.Clear();
            }
        }
        return false;
    }

    private static async Task<CustomerInternalIdempotencyRecord?> WaitForCompletionAsync(
        ApplicationDbContext context,
        string serviceId,
        string operation,
        string key,
        string requestHash,
        CustomerInternalServiceOptions options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow()
            .AddMilliseconds(options.InProgressWaitMilliseconds);
        while (timeProvider.GetUtcNow() < deadline)
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(options.InProgressPollMilliseconds),
                timeProvider,
                cancellationToken);
            context.ChangeTracker.Clear();
            var record = await context.CustomerInternalIdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    value => value.ServiceId == serviceId &&
                             value.Operation == operation &&
                             value.IdempotencyKey == key,
                    cancellationToken);
            if (record is null ||
                !string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
                return null;
            if (record.State == CustomerInternalIdempotencyState.Completed)
                return record;
        }
        return null;
    }

    private static async Task ReplayAsync(
        HttpContext context,
        CustomerInternalIdempotencyRecord record)
    {
        if (record.ResponseStatusCode is null ||
            string.IsNullOrWhiteSpace(record.ResponseContentType) ||
            record.ResponseBody is null)
        {
            await RejectAsync(
                context,
                503,
                InternalServiceProblemCodes.IdempotencyUnavailable,
                record.Operation);
            return;
        }
        context.Response.StatusCode = record.ResponseStatusCode.Value;
        context.Response.ContentType = record.ResponseContentType;
        await context.Response.Body.WriteAsync(record.ResponseBody, context.RequestAborted);
    }

    private static async Task<bool> CompleteWithRetryAsync(
        IServiceScopeFactory scopeFactory,
        Guid recordId,
        Guid ownerToken,
        int statusCode,
        string contentType,
        byte[] responseBody,
        DateTimeOffset lastConfirmedExpiryUtc,
        CustomerInternalServiceOptions options,
        TimeProvider timeProvider,
        IAppLogger logger)
    {
        var deadline = lastConfirmedExpiryUtc.AddMilliseconds(
            -options.InProgressLeaseSafetyMarginMilliseconds);
        while (timeProvider.GetUtcNow() < deadline)
        {
            var remaining = deadline - timeProvider.GetUtcNow();
            using var timeout = new CancellationTokenSource(
                Min(
                    remaining,
                    TimeSpan.FromMilliseconds(
                        options.InProgressLeaseOperationTimeoutMilliseconds)));
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var leaseService = scope.ServiceProvider
                    .GetRequiredService<ICustomerInternalIdempotencyLeaseService>();
                return await leaseService.CompleteAsync(
                    recordId,
                    ownerToken,
                    statusCode,
                    contentType,
                    responseBody,
                    timeout.Token);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException ||
                timeout.IsCancellationRequested)
            {
                logger.LogError(
                    "Customer internal idempotency completion attempt failed.",
                    exception);
                remaining = deadline - timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                    break;
                await Task.Delay(
                    Min(
                        remaining,
                        TimeSpan.FromMilliseconds(
                            options.InProgressLeaseRetryBackoffMilliseconds)),
                    timeProvider,
                    CancellationToken.None);
            }
        }
        return false;
    }

    internal static TimeSpan Min(TimeSpan first, TimeSpan second) =>
        first <= second ? first : second;

    private static string ComputeTransportHash(
        string method,
        string path,
        IEnumerable<KeyValuePair<string, string?>> query,
        string? contentType,
        byte[] body)
    {
        var bodyHash = InternalServiceCanonicalRequest.ComputeSha256Hex(body);
        var value = string.Join('\n',
            method.ToUpperInvariant(),
            InternalServiceCanonicalRequest.NormalizePathAndQuery(path, query),
            NormalizeContentType(contentType),
            bodyHash);
        return InternalServiceCanonicalRequest.ComputeSha256Hex(Encoding.UTF8.GetBytes(value));
    }

    private static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }
        if (!System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(
                contentType,
                out var parsed) ||
            string.IsNullOrWhiteSpace(parsed.MediaType))
        {
            return CreateBoundedContentTypeIdentity("invalid", contentType);
        }

        var mediaType = parsed.MediaType.Trim().ToLowerInvariant();
        var charset = parsed.CharSet?.Trim().Trim('"').ToLowerInvariant();
        if (mediaType.Length > 128 || charset?.Length > 64)
        {
            return CreateBoundedContentTypeIdentity("oversized", contentType);
        }
        return string.IsNullOrEmpty(charset)
            ? mediaType
            : $"{mediaType};charset={charset}";
    }

    private static string CreateBoundedContentTypeIdentity(
        string classification,
        string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(contentType);
        var digest = InternalServiceCanonicalRequest.ComputeSha256Hex(bytes);
        return $"{classification};length={bytes.Length};sha256={digest}";
    }

    private static byte[] CanonicalizeBody(byte[] body)
    {
        if (body.Length == 0) return body;
        try
        {
            using var document = JsonDocument.Parse(body);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonical(document.RootElement, writer);
            }
            return stream.ToArray();
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                    .OrderBy(value => value.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer))
                {
                    writer.WriteNumberValue(integer);
                }
                else if (element.TryGetDecimal(out var decimalValue))
                {
                    writer.WriteRawValue(
                        decimalValue.ToString(
                            "G29",
                            System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    writer.WriteRawValue(
                        element.GetDouble().ToString(
                            "R",
                            System.Globalization.CultureInfo.InvariantCulture));
                }
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool ValidSignature(string signature, string? secret, string canonical)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32 ||
            signature.Length != 64)
            return false;
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature);
        }
        catch (FormatException)
        {
            return false;
        }
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return CryptographicOperations.FixedTimeEquals(
            supplied,
            hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Header(HttpContext context, string name) =>
        context.Request.Headers[name].ToString();

    private static Task RejectAsync(
        HttpContext context,
        int status,
        string code,
        string operation)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.CacheControl = "no-store";
        object problem;
        if (operation is InternalServiceOperationNames.BookingStatusCallback
            or InternalServiceOperationNames.BookingStatusReconcile
            or InternalServiceOperationNames.BookingStatusRead)
        {
            var language = ConfigurationLanguageResolver.Resolve(
                context.Request.Query.ContainsKey("language")
                    ? context.Request.Query["language"].ToString()
                    : null,
                context.Request.Headers.AcceptLanguage.ToString());
            problem = BookingStatusProblemDetailsFactory.Create(
                status, code, language, context.TraceIdentifier);
        }
        else
        {
            problem = new
            {
                type = $"https://api.ghseeli.example/errors/{code}",
                title = "Internal service request was rejected.",
                status,
                code,
                correlationId = context.TraceIdentifier
            };
        }
        return context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }

}

public sealed class CustomerInternalServiceOptionsValidator :
    IValidateOptions<CustomerInternalServiceOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        CustomerInternalServiceOptions options)
    {
        var failures = new List<string>();
        if (options.InProgressRecoverySeconds < 1)
            failures.Add("InProgressRecoverySeconds must be at least 1.");
        if (!double.IsFinite(options.InProgressLeaseRenewalFraction) ||
            options.InProgressLeaseRenewalFraction <= 0)
        {
            failures.Add(
                "InProgressLeaseRenewalFraction must be finite and greater than 0.");
        }
        var leaseMilliseconds = options.InProgressRecoverySeconds * 1000d;
        var renewalMilliseconds =
            leaseMilliseconds * options.InProgressLeaseRenewalFraction;
        if (!double.IsFinite(renewalMilliseconds) || renewalMilliseconds < 100)
        {
            failures.Add(
                "The computed renewal interval must be finite and at least 100 milliseconds.");
        }
        if (options.InProgressLeaseSafetyMarginMilliseconds < 100)
        {
            failures.Add(
                "InProgressLeaseSafetyMarginMilliseconds must be at least 100.");
        }
        if (double.IsFinite(renewalMilliseconds) &&
            renewalMilliseconds >=
            leaseMilliseconds - options.InProgressLeaseSafetyMarginMilliseconds)
        {
            failures.Add(
                "InProgressLeaseRenewalFraction produces a computed renewal interval that must be strictly below the lease safety margin deadline.");
        }
        if (options.InProgressLeaseOperationTimeoutMilliseconds < 1)
        {
            failures.Add(
                "InProgressLeaseOperationTimeoutMilliseconds must be positive.");
        }
        if (options.InProgressLeaseRetryBackoffMilliseconds < 10)
        {
            failures.Add(
                "InProgressLeaseRetryBackoffMilliseconds must be at least 10.");
        }
        if (options.IdempotencyLifetimeSeconds <= options.InProgressRecoverySeconds)
        {
            failures.Add(
                "IdempotencyLifetimeSeconds must exceed InProgressRecoverySeconds.");
        }
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
