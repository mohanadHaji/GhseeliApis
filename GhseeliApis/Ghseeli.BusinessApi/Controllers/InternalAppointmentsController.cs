using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.InternalServices;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/internal/appointments")]
public class InternalAppointmentsController : ControllerBase
{
    private static readonly JsonSerializerOptions ResponseJsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    private readonly IAppointmentValidationService _service;
    private readonly IAvailableSlotsService _availableSlotsService;
    private readonly IAvailabilityDiscoveryService _availabilityDiscoveryService;

    public InternalAppointmentsController(
        IAppointmentValidationService service,
        IAvailableSlotsService availableSlotsService,
        IAvailabilityDiscoveryService availabilityDiscoveryService)
    {
        _service = service;
        _availableSlotsService = availableSlotsService;
        _availabilityDiscoveryService = availabilityDiscoveryService;
    }

    [HttpPost("availability-discovery")]
    [Authorize(Policy = BusinessPolicies.InternalAppointmentAvailabilityDiscovery)]
    [InternalServiceOperation(InternalServiceOperationNames.AppointmentAvailabilityDiscovery)]
    [ProducesResponseType<AvailabilityDiscoveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> DiscoverAvailability(
        AvailabilityDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return JsonResponse(
                StatusCodes.Status200OK,
                await _availabilityDiscoveryService.DiscoverAsync(
                    request,
                    cancellationToken));
        }
        catch (AvailabilityValidationException exception)
        {
            return JsonResponse(StatusCodes.Status400BadRequest, new
            {
                message = exception.Message,
                errors = exception.Errors
            });
        }
    }

    [HttpPost("available-slots")]
    [Authorize(Policy = BusinessPolicies.InternalAppointmentAvailableSlots)]
    [InternalServiceOperation(InternalServiceOperationNames.AppointmentAvailableSlots)]
    public async Task<IActionResult> GetAvailableSlots(
        AvailableSlotsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return JsonResponse(
                StatusCodes.Status200OK,
                await _availableSlotsService.GetAsync(request, cancellationToken));
        }
        catch (AvailabilityValidationException exception)
        {
            return JsonResponse(StatusCodes.Status400BadRequest, new
            {
                message = exception.Message,
                errors = exception.Errors
            });
        }
    }

    [HttpPost("validate")]
    [Authorize(Policy = BusinessPolicies.InternalAppointmentValidate)]
    [InternalServiceOperation(InternalServiceOperationNames.AppointmentValidate)]
    public async Task<IActionResult> Validate(ValidateAppointmentRequest request)
    {
        try
        {
            return JsonResponse(
                StatusCodes.Status200OK,
                await _service.ValidateAsync(request));
        }
        catch (AvailabilityValidationException exception)
        {
            return JsonResponse(StatusCodes.Status400BadRequest, new
            {
                message = exception.Message,
                errors = exception.Errors
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private static ContentResult JsonResponse(int statusCode, object value)
    {
        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(value, ResponseJsonOptions)
        };
    }

}
