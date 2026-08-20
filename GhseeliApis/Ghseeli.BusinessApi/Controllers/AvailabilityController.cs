using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Availability;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/business/availability")]
[Authorize(Policy = BusinessPolicies.OwnerOrAdmin)]
public class AvailabilityController : ControllerBase
{
    private static readonly JsonSerializerOptions ResponseJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

    private readonly IAvailabilityManagementService _service;

    public AvailabilityController(IAvailabilityManagementService service)
    {
        _service = service;
    }

    [HttpGet("branches/{branchId:guid}/settings")]
    public Task<IActionResult> GetSettings(Guid branchId)
    {
        return ExecuteAsync(async () =>
        {
            var response = await _service.GetSettingsAsync(GetUserId(), IsAdmin(), branchId);
            return response is null
                ? JsonResponse(StatusCodes.Status404NotFound, new { message = "The branch availability settings were not found." })
                : JsonResponse(StatusCodes.Status200OK, response);
        });
    }

    [HttpPut("branches/{branchId:guid}/settings")]
    public Task<IActionResult> UpsertSettings(
        Guid branchId,
        UpdateBranchAvailabilitySettingsRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _service.UpsertSettingsAsync(
            GetUserId(),
            IsAdmin(),
            branchId,
            request)));
    }

    [HttpGet("branches/{branchId:guid}/recurring-schedules")]
    public Task<IActionResult> GetRecurringSchedules(Guid branchId)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _service.GetRecurringSchedulesAsync(
            GetUserId(),
            IsAdmin(),
            branchId)));
    }

    [HttpGet("recurring-schedules/{scheduleId:guid}")]
    public Task<IActionResult> GetRecurringSchedule(Guid scheduleId)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _service.GetRecurringScheduleAsync(
            GetUserId(),
            IsAdmin(),
            scheduleId)));
    }

    [HttpPost("branches/{branchId:guid}/recurring-schedules")]
    public Task<IActionResult> CreateRecurringSchedule(
        Guid branchId,
        CreateRecurringScheduleRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _service.CreateRecurringScheduleAsync(
            GetUserId(),
            IsAdmin(),
            branchId,
            request)));
    }

    [HttpPut("recurring-schedules/{scheduleId:guid}")]
    public Task<IActionResult> UpdateRecurringSchedule(
        Guid scheduleId,
        UpdateRecurringScheduleRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _service.UpdateRecurringScheduleAsync(
            GetUserId(),
            IsAdmin(),
            scheduleId,
            request)));
    }

    [HttpDelete("recurring-schedules/{scheduleId:guid}")]
    public Task<IActionResult> DeleteRecurringSchedule(Guid scheduleId)
    {
        return ExecuteAsync(async () =>
        {
            await _service.DeleteRecurringScheduleAsync(GetUserId(), IsAdmin(), scheduleId);
            return NoContent();
        });
    }

    [HttpGet("branches/{branchId:guid}/date-overrides")]
    public Task<IActionResult> GetAvailabilityOverrides(
        Guid branchId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _service.GetAvailabilityOverridesAsync(
            GetUserId(),
            IsAdmin(),
            branchId,
            fromDate,
            toDate)));
    }

    [HttpGet("date-overrides/{overrideId:guid}")]
    public Task<IActionResult> GetAvailabilityOverride(Guid overrideId)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _service.GetAvailabilityOverrideAsync(
            GetUserId(),
            IsAdmin(),
            overrideId)));
    }

    [HttpPost("branches/{branchId:guid}/date-overrides")]
    public Task<IActionResult> CreateAvailabilityOverride(
        Guid branchId,
        CreateAvailabilityOverrideRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _service.CreateAvailabilityOverrideAsync(
            GetUserId(),
            IsAdmin(),
            branchId,
            request)));
    }

    [HttpPut("date-overrides/{overrideId:guid}")]
    public Task<IActionResult> UpdateAvailabilityOverride(
        Guid overrideId,
        UpdateAvailabilityOverrideRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _service.UpdateAvailabilityOverrideAsync(
            GetUserId(),
            IsAdmin(),
            overrideId,
            request)));
    }

    [HttpDelete("date-overrides/{overrideId:guid}")]
    public Task<IActionResult> DeleteAvailabilityOverride(Guid overrideId)
    {
        return ExecuteAsync(async () =>
        {
            await _service.DeleteAvailabilityOverrideAsync(GetUserId(), IsAdmin(), overrideId);
            return NoContent();
        });
    }

    [HttpGet("branches/{branchId:guid}/service-area")]
    public Task<IActionResult> GetServiceArea(Guid branchId)
    {
        return ExecuteAsync(async () =>
        {
            var response = await _service.GetServiceAreaAsync(GetUserId(), IsAdmin(), branchId);
            return response is null
                ? JsonResponse(StatusCodes.Status404NotFound, new { message = "The service area was not found." })
                : JsonResponse(StatusCodes.Status200OK, response);
        });
    }

    [HttpPut("branches/{branchId:guid}/service-area")]
    public Task<IActionResult> UpsertServiceArea(
        Guid branchId,
        UpsertBranchServiceAreaRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _service.UpsertServiceAreaAsync(
            GetUserId(),
            IsAdmin(),
            branchId,
            request)));
    }

    [HttpDelete("branches/{branchId:guid}/service-area")]
    public Task<IActionResult> DeleteServiceArea(Guid branchId)
    {
        return ExecuteAsync(async () =>
        {
            await _service.DeleteServiceAreaAsync(GetUserId(), IsAdmin(), branchId);
            return NoContent();
        });
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
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
        catch (BusinessConflictException exception)
        {
            return JsonResponse(StatusCodes.Status409Conflict, new
            {
                message = exception.Message,
                errors = exception.Errors
            });
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
