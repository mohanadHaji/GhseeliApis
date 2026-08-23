using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.InternalServices;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/internal/reservations")]
[Authorize(Policy = BusinessPolicies.InternalReservationCreate)]
public sealed class InternalReservationsController : ControllerBase
{
    private readonly IReservationService _service;

    public InternalReservationsController(IReservationService service)
    {
        _service = service;
    }

    [HttpPost]
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
            var statusCode = exception.Code == ReservationErrorCodes.Invalid
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
}
