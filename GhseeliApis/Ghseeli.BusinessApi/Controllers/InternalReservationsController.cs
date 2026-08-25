using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.InternalServices;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.BusinessApi.Services;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/internal/reservations")]
public sealed class InternalReservationsController : ControllerBase
{
    private readonly IReservationService _service;
    private readonly IBookingStatusService _statusService;

    public InternalReservationsController(
        IReservationService service,
        IBookingStatusService statusService)
    {
        _service = service;
        _statusService = statusService;
    }

    [HttpPost]
    [Authorize(Policy = BusinessPolicies.InternalReservationCreate)]
    [InternalServiceOperation(InternalServiceOperationNames.ReservationCreate)]
    public async Task<IActionResult> Create(
        CreateReservationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.CreateAsync(request, cancellationToken));
        }
        catch (ReservationRejectedException exception)
        {
            var statusCode = exception.Code == ReservationErrorCodes.Invalid &&
                !exception.IsStateConflict
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status409Conflict;
            var result = new ObjectResult(new InternalServiceProblemResponse
            {
                Type = $"https://api.ghseeli.example/errors/{exception.Code}",
                Title = "Reservation request was rejected.",
                Status = statusCode,
                Detail = exception.Message,
                Code = exception.Code,
                CorrelationId = HttpContext.GetCorrelationId()
            })
            {
                StatusCode = statusCode
            };
            result.ContentTypes.Add("application/problem+json");
            return result;
        }
    }

    [HttpGet("{reference:guid}")]
    [Authorize(Policy = BusinessPolicies.InternalReservationStatusRead)]
    [InternalServiceOperation(InternalServiceOperationNames.ReservationStatusRead)]
    public async Task<IActionResult> GetStatus(
        Guid reference,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var response = await _statusService.GetAuthoritativeAsync(reference, cancellationToken);
        if (response is not null)
        {
            return Ok(response);
        }
        var result = new ObjectResult(new InternalServiceProblemResponse
        {
            Type = $"https://api.ghseeli.example/errors/{BookingStatusErrorCodes.NotFound}",
            Title = "Booking status request was rejected.",
            Status = StatusCodes.Status404NotFound,
            Detail = "The booking reference was not found.",
            Code = BookingStatusErrorCodes.NotFound,
            CorrelationId = HttpContext.GetCorrelationId()
        })
        {
            StatusCode = StatusCodes.Status404NotFound
        };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
