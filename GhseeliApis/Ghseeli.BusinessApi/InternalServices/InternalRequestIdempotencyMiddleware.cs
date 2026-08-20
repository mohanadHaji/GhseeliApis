using Ghseeli.BusinessApi.Constants;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;

namespace Ghseeli.BusinessApi.InternalServices;

public sealed class InternalRequestIdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private const string ResponseUnavailableTitle = "Internal request result is unavailable.";
    private const string ResponseUnavailableDetail =
        "The internal response could not be safely stored for idempotent replay.";

    public InternalRequestIdempotencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IInternalIdempotencyStore store,
        IOptions<InternalServiceAuthenticationOptions> optionsAccessor,
        Services.Availability.ISystemClock clock,
        IAppLogger logger)
    {
        var endpointOperation = context.GetEndpoint()?
            .Metadata
            .GetMetadata<InternalServiceOperationAttribute>()?
            .Operation;

        if (!string.Equals(
                endpointOperation,
                InternalServiceOperationNames.AppointmentValidate,
                StringComparison.Ordinal))
        {
            await _next(context);
            return;
        }

        var operation = endpointOperation!;

        if (!(context.User.Identity?.IsAuthenticated ?? false))
        {
            await _next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers[InternalServiceWireConstants.IdempotencyKeyHeaderName]
            .ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await InternalServiceProblemResponseFactory.WriteAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Idempotency key is required.",
                "Mutating internal service requests must supply an Idempotency-Key header.",
                InternalServiceProblemCodes.IdempotencyKeyRequired);
            return;
        }

        if (!InternalServiceHeaderValueValidator.IsValidIdempotencyKey(idempotencyKey))
        {
            await InternalServiceProblemResponseFactory.WriteAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Idempotency key is invalid.",
                "The supplied Idempotency-Key header is invalid.",
                InternalServiceProblemCodes.IdempotencyKeyInvalid);
            return;
        }

        var options = optionsAccessor.Value;
        string requestHash;
        try
        {
            requestHash = await InternalServiceRequestBodyAccessor.GetBodyHashAsync(
                context,
                options,
                context.RequestAborted);
        }
        catch (InternalRequestBodyTooLargeException)
        {
            await InternalServiceProblemResponseFactory.WriteAsync(
                context,
                StatusCodes.Status413PayloadTooLarge,
                "Internal request body is too large.",
                "The request body exceeds the allowed size.",
                InternalServiceProblemCodes.RequestBodyTooLarge);
            return;
        }

        var serviceId = context.User.FindFirstValue(BusinessClaimTypes.InternalServiceId)
            ?? string.Empty;
        var nowUtc = clock.UtcNow;
        var claim = await store.ClaimAsync(
            serviceId,
            operation,
            idempotencyKey,
            requestHash,
            nowUtc,
            nowUtc.AddSeconds(options.IdempotencyLifetimeSeconds),
            context.RequestAborted);

        switch (claim.Kind)
        {
            case InternalIdempotencyClaimResultKind.Conflict:
                logger.LogWarning(
                    $"Internal request idempotency conflict. ServiceId={serviceId}, Operation={endpointOperation}, CorrelationId={context.GetCorrelationId()}.");
                await InternalServiceProblemResponseFactory.WriteAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "Idempotent request conflicted with prior content.",
                    "The supplied Idempotency-Key was already used for a different request body.",
                    InternalServiceProblemCodes.IdempotencyConflict);
                return;

            case InternalIdempotencyClaimResultKind.Replay:
                await ReplayAsync(context, claim.Record!);
                return;

            case InternalIdempotencyClaimResultKind.InProgress:
                var completedRecord = await store.WaitForCompletionAsync(
                    serviceId,
                    operation,
                    idempotencyKey,
                    TimeSpan.FromMilliseconds(options.InProgressWaitMilliseconds),
                    TimeSpan.FromMilliseconds(options.InProgressPollMilliseconds),
                    context.RequestAborted);
                if (completedRecord is not null)
                {
                    await ReplayAsync(context, completedRecord);
                    return;
                }

                logger.LogWarning(
                    $"Internal request idempotency wait timed out. ServiceId={serviceId}, Operation={endpointOperation}, CorrelationId={context.GetCorrelationId()}.");
                await InternalServiceProblemResponseFactory.WriteAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "Idempotent request result is unavailable.",
                    "The original request is still in progress. Retry with the same idempotency key later.",
                    InternalServiceProblemCodes.IdempotencyUnavailable);
                return;

            case InternalIdempotencyClaimResultKind.Acquired:
                await ExecuteAndStoreAsync(
                    context,
                    claim.RecordId!.Value,
                    store,
                    clock,
                    options,
                    logger);
                return;

            default:
                await _next(context);
                return;
        }
    }

    private async Task ExecuteAndStoreAsync(
        HttpContext context,
        Guid recordId,
        IInternalIdempotencyStore store,
        Services.Availability.ISystemClock clock,
        InternalServiceAuthenticationOptions options,
        IAppLogger logger)
    {
        var originalResponseBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        var claimResolved = false;

        try
        {
            await _next(context);

            buffer.Position = 0;
            var responseBody = await new StreamReader(buffer, Encoding.UTF8, leaveOpen: true)
                .ReadToEndAsync(context.RequestAborted);
            var bodyLength = Encoding.UTF8.GetByteCount(responseBody);
            if (bodyLength > options.MaxStoredResponseBytes)
            {
                logger.LogError(
                    $"Internal request response exceeded the configured idempotency storage limit. CorrelationId={context.GetCorrelationId()}.");
                claimResolved = await ReleaseClaimSafelyAsync(store, recordId, logger);
                context.Response.Body = originalResponseBody;
                context.Response.Clear();
                await InternalServiceProblemResponseFactory.WriteAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    ResponseUnavailableTitle,
                    ResponseUnavailableDetail,
                    InternalServiceProblemCodes.IdempotencyUnavailable);
                return;
            }

            var contentType = context.Response.ContentType ?? "application/json";
            await store.CompleteAsync(
                recordId,
                context.Response.StatusCode,
                contentType,
                responseBody,
                clock.UtcNow,
                context.RequestAborted);
            claimResolved = true;

            buffer.Position = 0;
            await buffer.CopyToAsync(originalResponseBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalResponseBody;

            if (!claimResolved)
            {
                await ReleaseClaimSafelyAsync(store, recordId, logger);
            }
        }
    }

    private static async Task ReplayAsync(
        HttpContext context,
        Models.InternalServiceIdempotencyRecord record)
    {
        context.Response.StatusCode = record.StatusCode ?? StatusCodes.Status200OK;
        context.Response.ContentType = record.ContentType ?? "application/json";

        if (!string.IsNullOrEmpty(record.ResponseBody))
        {
            await context.Response.WriteAsync(record.ResponseBody, context.RequestAborted);
        }
    }

    private static async Task<bool> ReleaseClaimSafelyAsync(
        IInternalIdempotencyStore store,
        Guid recordId,
        IAppLogger logger)
    {
        try
        {
            await store.ReleaseAsync(recordId, CancellationToken.None);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Failed to release an internal idempotency claim after an incomplete execution.",
                exception);
            return false;
        }
    }
}
