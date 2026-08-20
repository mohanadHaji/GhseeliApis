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
[Authorize(Policy = BusinessPolicies.InternalAppointmentValidate)]
public class InternalAppointmentsController : ControllerBase
{
    private static readonly JsonSerializerOptions ResponseJsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    private readonly IAppointmentValidationService _service;

    public InternalAppointmentsController(IAppointmentValidationService service)
    {
        _service = service;
    }

    [HttpPost("validate")]
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
