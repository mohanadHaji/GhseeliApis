using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.Middleware;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Internal;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/v1/internal/bookings")]
[AllowWithoutDeviceToken]
public sealed class InternalBookingStatusController : ControllerBase
{
    private readonly IBookingStatusInboxService _service;
    public InternalBookingStatusController(IBookingStatusInboxService service) => _service = service;

    [HttpPost("status")]
    [CustomerInternalOperation(InternalServiceOperationNames.BookingStatusCallback)]
    public async Task<IActionResult> Apply(
        BookingStatusChangedMessage? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ProblemResult(
                StatusCodes.Status400BadRequest,
                BookingStatusErrorCodes.Invalid);
        }
        try
        {
            return Ok(await _service.ApplyAsync(
                request,
                (string)HttpContext.Items[CustomerInternalServiceMiddleware.RequestHashKey]!,
                false,
                HttpContext.TraceIdentifier,
                cancellationToken));
        }
        catch (BookingStatusInboxException exception)
        {
            return ProblemResult(
                exception.Code switch
                {
                    BookingStatusErrorCodes.Invalid => StatusCodes.Status400BadRequest,
                    BookingStatusErrorCodes.NotFound => StatusCodes.Status404NotFound,
                    _ => StatusCodes.Status409Conflict
                },
                exception.Code,
                exception.Message);
        }
    }

    [HttpPost("{reference:guid}/reconcile")]
    [CustomerInternalOperation(InternalServiceOperationNames.BookingStatusReconcile)]
    public async Task<IActionResult> Reconcile(
        Guid reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.ReconcileAsync(
                reference,
                HttpContext.TraceIdentifier,
                cancellationToken));
        }

        catch (BookingStatusInboxException exception)
        {
            return ProblemResult(
                exception.Code switch
                {
                    BookingStatusErrorCodes.Invalid => StatusCodes.Status502BadGateway,
                    BookingStatusErrorCodes.NotFound => StatusCodes.Status404NotFound,
                    _ => StatusCodes.Status409Conflict
                },
                exception.Code,
                exception.Message);
        }
        catch (BusinessApiException)
        {
            return ProblemResult(
                StatusCodes.Status503ServiceUnavailable,
                InternalServiceProblemCodes.IdempotencyUnavailable,
                "The authoritative booking status is temporarily unavailable.");
        }
    }

    [HttpGet("{reference:guid}")]
    [CustomerInternalOperation(InternalServiceOperationNames.BookingStatusRead)]
    public async Task<IActionResult> GetCurrent(
        Guid reference,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetCurrentAsync(reference, cancellationToken);
        return result is null
            ? ProblemResult(404, BookingStatusErrorCodes.NotFound, "The booking reference was not found.")
            : Ok(result);
    }

    [HttpPost("{unmatched}")]
    public IActionResult UnknownPost(string unmatched) => NotFound();

    private ObjectResult ProblemResult(int status, string code, string? detail = null)
    {
        Response.Headers.CacheControl = "no-store";
        return new(BookingStatusProblemDetailsFactory.Create(
            status,
            code,
            ConfigurationLanguageResolver.Arabic,
            HttpContext.TraceIdentifier,
            detail))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }

}
