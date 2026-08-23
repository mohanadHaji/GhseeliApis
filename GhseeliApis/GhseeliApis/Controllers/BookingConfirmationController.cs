using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Booking;
using GhseeliApis.Filters;
using GhseeliApis.Middleware;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/v1/bookings")]
[Authorize(Policy = "UserPolicy")]
public sealed class BookingConfirmationController : ControllerBase
{
    private const long MaxRequestBodyBytes = 65_536;
    private const string OrderGuidHeaderName = "X-Order-Guid";
    private readonly IBookingConfirmationService _service;
    private readonly IAppLogger _logger;

    public BookingConfirmationController(
        IBookingConfirmationService service,
        IAppLogger logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("from-draft")]
    [EnforceJsonRequestContentType]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnforceRequestBodySizeLimit(
        MaxRequestBodyBytes,
        BookingConfirmationProblemCodes.RequestBodyTooLarge)]
    [ProducesResponseType<ConfirmedBookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmBookingFromDraftRequest? request,
        [FromHeader(Name = OrderGuidHeaderName)] string? orderGuid,
        [FromQuery] string? language,
        [FromHeader(Name = "Accept-Language")] string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(orderGuid, out var parsedOrderGuid) ||
            parsedOrderGuid == Guid.Empty ||
            request is null ||
            request.ExpectedVersion <= 0 ||
            !request.CancellationPolicyAcknowledged)
        {
            return ProblemResult(
                StatusCodes.Status400BadRequest,
                BookingConfirmationProblemCodes.Invalid,
                language,
                acceptLanguage);
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            userId == Guid.Empty)
        {
            return ProblemResult(
                StatusCodes.Status401Unauthorized,
                BookingConfirmationProblemCodes.Invalid,
                language,
                acceptLanguage);
        }

        var deviceId = HttpContext.GetDeviceId();
        if (!deviceId.HasValue)
        {
            return ProblemResult(
                StatusCodes.Status401Unauthorized,
                BookingConfirmationProblemCodes.Invalid,
                language,
                acceptLanguage);
        }

        try
        {
            return Ok(await _service.ConfirmAsync(
                parsedOrderGuid,
                request,
                deviceId.Value,
                userId,
                language,
                acceptLanguage,
                cancellationToken));
        }
        catch (BookingConfirmationException exception)
        {
            _logger.LogWarning(
                $"Booking confirmation failed with code {exception.Code} for draft {parsedOrderGuid:D}.");
            return ProblemResult(
                exception.StatusCode,
                exception.Code,
                language,
                acceptLanguage,
                exception.BusinessErrorCode);
        }
    }

    private ObjectResult ProblemResult(
        int statusCode,
        string code,
        string? requestedLanguage,
        string? acceptLanguage,
        string? businessErrorCode = null)
    {
        var resolvedLanguage = ConfigurationLanguageResolver.Resolve(
            requestedLanguage,
            acceptLanguage);
        return new ObjectResult(BookingConfirmationProblemDetailsFactory.Create(
            statusCode,
            code,
            resolvedLanguage,
            HttpContext.TraceIdentifier,
            businessErrorCode))
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}
