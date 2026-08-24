using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.InternalServices;
using Ghseeli.BusinessApi.Services;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/business/work-orders")]
[Authorize(Policy = BusinessPolicies.BusinessMember)]
public sealed class WorkOrdersController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();
    private readonly IBookingStatusService _service;

    public WorkOrdersController(IBookingStatusService service) => _service = service;

    [HttpPost("{id:guid}/transitions")]
    public async Task<IActionResult> Transition(
        Guid id,
        TransitionWorkOrderRequest? request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId))
        {
            await BusinessAuthenticationProblemResponseFactory.WriteAsync(
                HttpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication is required.",
                "A valid Business API access token is required.",
                BusinessAuthenticationProblemCodes.AuthenticationRequired);
            return new EmptyResult();
        }
        if (request is null)
        {
            return ProblemResult(
                StatusCodes.Status400BadRequest,
                BookingStatusErrorCodes.Invalid,
                "The transition request is invalid.");
        }
        var idempotencyValues = Request.Headers[
            InternalServiceWireConstants.IdempotencyKeyHeaderName];
        var idempotencyKey = idempotencyValues.ToString();
        if (idempotencyValues.Count != 1 ||
            !InternalServiceHeaderValueValidator.IsValidIdempotencyKey(idempotencyKey))
        {
            return ProblemResult(
                StatusCodes.Status400BadRequest,
                BookingStatusErrorCodes.Invalid,
                "A valid Idempotency-Key header is required.");
        }
        try
        {
            var response = await _service.TransitionAsync(
                userId,
                User.IsInRole(BusinessRoles.Admin),
                id,
                request.Status,
                HttpContext.TraceIdentifier,
                cancellationToken,
                idempotencyKey);
            return response is null
                ? ProblemResult(
                    StatusCodes.Status404NotFound,
                    BookingStatusErrorCodes.NotFound,
                    "The work order was not found.")
                : Ok(response);
        }
        catch (BookingStatusRejectedException exception)
        {
            return ProblemResult(
                exception.Code == BookingStatusErrorCodes.Invalid
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status409Conflict,
                exception.Code,
                exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProblemResult(
                StatusCodes.Status409Conflict,
                BookingStatusErrorCodes.TransitionConflict,
                "The work order changed concurrently. Refresh it and retry the transition.");
        }
        catch (DbUpdateException)
        {
            return ProblemResult(
                StatusCodes.Status409Conflict,
                BookingStatusErrorCodes.TransitionConflict,
                "The idempotency key conflicts with a different transition request.");
        }
    }

    private ContentResult ProblemResult(int status, string code, string detail) =>
        new()
        {
            StatusCode = status,
            ContentType = "application/problem+json",
            Content = JsonSerializer.Serialize(new
            {
                type = $"https://api.ghseeli.example/errors/{code}",
                title = "Work-order status request was rejected.",
                status,
                detail,
                code,
                correlationId = HttpContext.TraceIdentifier
            }, JsonOptions)
        };
}
