using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/internal/catalog")]
[Authorize(Policy = BusinessPolicies.Step5TemporaryInternalOwnerOrAdmin)]
public class InternalCatalogController : ControllerBase
{
    private static readonly JsonSerializerOptions ResponseJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

    private readonly ICatalogPublicationService _service;

    public InternalCatalogController(ICatalogPublicationService service)
    {
        _service = service;
    }

    [HttpGet("snapshot")]
    public async Task<IActionResult> GetSnapshot([FromQuery] Guid? companyId)
    {
        try
        {
            return JsonResponse(
                StatusCodes.Status200OK,
                await _service.GetSnapshotAsync(GetUserId(), IsAdmin(), companyId));
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

    private Guid GetUserId()
    {
        return Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }

    private bool IsAdmin()
    {
        return User.IsInRole(BusinessRoles.Admin);
    }
}
