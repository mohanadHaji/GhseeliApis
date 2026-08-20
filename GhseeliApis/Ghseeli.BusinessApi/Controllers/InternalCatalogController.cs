using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.InternalServices;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/internal/catalog")]
[Authorize(Policy = BusinessPolicies.InternalCatalogRead)]
public class InternalCatalogController : ControllerBase
{
    private static readonly JsonSerializerOptions ResponseJsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    private readonly ICatalogPublicationService _service;

    public InternalCatalogController(ICatalogPublicationService service)
    {
        _service = service;
    }

    [HttpGet("snapshot")]
    [InternalServiceOperation(InternalServiceOperationNames.CatalogSnapshot)]
    public async Task<IActionResult> GetSnapshot([FromQuery] Guid? companyId)
    {
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            return JsonResponse(StatusCodes.Status400BadRequest, new
            {
                code = "company_id_required",
                message = "Internal catalog snapshot requests must supply a companyId query value."
            });
        }

        try
        {
            return JsonResponse(
                StatusCodes.Status200OK,
                await _service.GetSnapshotAsync(companyId.Value));
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
        catch (KeyNotFoundException exception)
        {
            return JsonResponse(StatusCodes.Status404NotFound, new { message = exception.Message });
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
