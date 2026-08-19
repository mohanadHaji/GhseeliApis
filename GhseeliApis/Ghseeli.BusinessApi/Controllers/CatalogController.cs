using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.Common.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghseeli.BusinessApi.Controllers;

/// <summary>
/// Manages the localized business catalog for categories, offerings, and configurable add-ons.
/// </summary>
[ApiController]
[Route("api/v1/business/catalog")]
[Authorize(Policy = BusinessPolicies.OwnerOrAdmin)]
public class CatalogController : ControllerBase
{
    private static readonly JsonSerializerOptions ResponseJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

    private readonly ICatalogService _catalogService;
    private readonly IAppLogger _logger;

    public CatalogController(ICatalogService catalogService, IAppLogger logger)
    {
        _catalogService = catalogService;
        _logger = logger;
    }

    /// <summary>
    /// Lists localized service categories for the caller's company, or the specified company for admins.
    /// </summary>
    [HttpGet("categories")]
    public Task<IActionResult> GetCategories([FromQuery] Guid? companyId)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.GetCategoriesAsync(
            GetUserId(),
            IsAdmin(),
            companyId)));
    }

    /// <summary>
    /// Gets one localized service category with its offerings.
    /// </summary>
    [HttpGet("categories/{categoryId:guid}")]
    public Task<IActionResult> GetCategory(Guid categoryId)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.GetCategoryAsync(
            GetUserId(),
            IsAdmin(),
            categoryId)));
    }

    /// <summary>
    /// Creates a localized service category.
    /// </summary>
    [HttpPost("categories")]
    public Task<IActionResult> CreateCategory(CreateServiceCategoryRequest request)
    {
        return ExecuteAsync(async () =>
        {
            var category = await _catalogService.CreateCategoryAsync(
                GetUserId(),
                IsAdmin(),
                request);
            return JsonCreated($"/api/v1/business/catalog/categories/{category.Id}", category);
        });
    }

    /// <summary>
    /// Updates a localized service category.
    /// </summary>
    [HttpPut("categories/{categoryId:guid}")]
    public Task<IActionResult> UpdateCategory(
        Guid categoryId,
        UpdateServiceCategoryRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.UpdateCategoryAsync(
            GetUserId(),
            IsAdmin(),
            categoryId,
            request)));
    }

    /// <summary>
    /// Deletes a localized service category.
    /// </summary>
    [HttpDelete("categories/{categoryId:guid}")]
    public Task<IActionResult> DeleteCategory(Guid categoryId)
    {
        return ExecuteAsync(async () =>
        {
            await _catalogService.DeleteCategoryAsync(GetUserId(), IsAdmin(), categoryId);
            return NoContent();
        });
    }

    /// <summary>
    /// Lists offerings for the caller's company, or the specified company for admins.
    /// </summary>
    [HttpGet("offerings")]
    public Task<IActionResult> GetOfferings(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? categoryId,
        [FromQuery] Guid? branchId)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.GetOfferingsAsync(
            GetUserId(),
            IsAdmin(),
            companyId,
            categoryId,
            branchId)));
    }

    /// <summary>
    /// Gets one offering with its localized add-on configuration.
    /// </summary>
    [HttpGet("offerings/{offeringId:guid}")]
    public Task<IActionResult> GetOffering(Guid offeringId)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.GetOfferingAsync(
            GetUserId(),
            IsAdmin(),
            offeringId)));
    }

    /// <summary>
    /// Creates a localized service offering.
    /// </summary>
    [HttpPost("offerings")]
    public Task<IActionResult> CreateOffering(CreateServiceOfferingRequest request)
    {
        return ExecuteAsync(async () =>
        {
            var offering = await _catalogService.CreateOfferingAsync(
                GetUserId(),
                IsAdmin(),
                request);
            return JsonCreated($"/api/v1/business/catalog/offerings/{offering.Id}", offering);
        });
    }

    /// <summary>
    /// Updates a localized service offering.
    /// </summary>
    [HttpPut("offerings/{offeringId:guid}")]
    public Task<IActionResult> UpdateOffering(
        Guid offeringId,
        UpdateServiceOfferingRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.UpdateOfferingAsync(
            GetUserId(),
            IsAdmin(),
            offeringId,
            request)));
    }

    /// <summary>
    /// Deletes a localized service offering.
    /// </summary>
    [HttpDelete("offerings/{offeringId:guid}")]
    public Task<IActionResult> DeleteOffering(Guid offeringId)
    {
        return ExecuteAsync(async () =>
        {
            await _catalogService.DeleteOfferingAsync(GetUserId(), IsAdmin(), offeringId);
            return NoContent();
        });
    }

    /// <summary>
    /// Lists add-on groups for an offering.
    /// </summary>
    [HttpGet("offerings/{offeringId:guid}/addon-groups")]
    public Task<IActionResult> GetAddonGroups(Guid offeringId)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.GetAddonGroupsAsync(
            GetUserId(),
            IsAdmin(),
            offeringId)));
    }

    /// <summary>
    /// Gets one add-on group with its choices.
    /// </summary>
    [HttpGet("addon-groups/{addonGroupId:guid}")]
    public Task<IActionResult> GetAddonGroup(Guid addonGroupId)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.GetAddonGroupAsync(
            GetUserId(),
            IsAdmin(),
            addonGroupId)));
    }

    /// <summary>
    /// Creates an add-on group for an offering.
    /// </summary>
    [HttpPost("offerings/{offeringId:guid}/addon-groups")]
    public Task<IActionResult> CreateAddonGroup(
        Guid offeringId,
        CreateAddonGroupRequest request)
    {
        return ExecuteAsync(async () =>
        {
            var addonGroup = await _catalogService.CreateAddonGroupAsync(
                GetUserId(),
                IsAdmin(),
                offeringId,
                request);
            return JsonCreated(
                $"/api/v1/business/catalog/addon-groups/{addonGroup.Id}",
                addonGroup);
        });
    }

    /// <summary>
    /// Updates an add-on group and its selection rules.
    /// </summary>
    [HttpPut("addon-groups/{addonGroupId:guid}")]
    public Task<IActionResult> UpdateAddonGroup(
        Guid addonGroupId,
        UpdateAddonGroupRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.UpdateAddonGroupAsync(
            GetUserId(),
            IsAdmin(),
            addonGroupId,
            request)));
    }

    /// <summary>
    /// Deletes an add-on group.
    /// </summary>
    [HttpDelete("addon-groups/{addonGroupId:guid}")]
    public Task<IActionResult> DeleteAddonGroup(Guid addonGroupId)
    {
        return ExecuteAsync(async () =>
        {
            await _catalogService.DeleteAddonGroupAsync(GetUserId(), IsAdmin(), addonGroupId);
            return NoContent();
        });
    }

    /// <summary>
    /// Lists choices for an add-on group.
    /// </summary>
    [HttpGet("addon-groups/{addonGroupId:guid}/choices")]
    public Task<IActionResult> GetAddonChoices(Guid addonGroupId)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.GetAddonChoicesAsync(
            GetUserId(),
            IsAdmin(),
            addonGroupId)));
    }

    /// <summary>
    /// Gets one add-on choice.
    /// </summary>
    [HttpGet("addon-choices/{addonChoiceId:guid}")]
    public Task<IActionResult> GetAddonChoice(Guid addonChoiceId)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.GetAddonChoiceAsync(
            GetUserId(),
            IsAdmin(),
            addonChoiceId)));
    }

    /// <summary>
    /// Creates an add-on choice for a group.
    /// </summary>
    [HttpPost("addon-groups/{addonGroupId:guid}/choices")]
    public Task<IActionResult> CreateAddonChoice(
        Guid addonGroupId,
        CreateAddonChoiceRequest request)
    {
        return ExecuteAsync(async () =>
        {
            var addonChoice = await _catalogService.CreateAddonChoiceAsync(
                GetUserId(),
                IsAdmin(),
                addonGroupId,
                request);
            return JsonCreated(
                $"/api/v1/business/catalog/addon-choices/{addonChoice.Id}",
                addonChoice);
        });
    }

    /// <summary>
    /// Updates an add-on choice.
    /// </summary>
    [HttpPut("addon-choices/{addonChoiceId:guid}")]
    public Task<IActionResult> UpdateAddonChoice(
        Guid addonChoiceId,
        UpdateAddonChoiceRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(StatusCodes.Status200OK, await _catalogService.UpdateAddonChoiceAsync(
            GetUserId(),
            IsAdmin(),
            addonChoiceId,
            request)));
    }

    /// <summary>
    /// Deletes an add-on choice.
    /// </summary>
    [HttpDelete("addon-choices/{addonChoiceId:guid}")]
    public Task<IActionResult> DeleteAddonChoice(Guid addonChoiceId)
    {
        return ExecuteAsync(async () =>
        {
            await _catalogService.DeleteAddonChoiceAsync(GetUserId(), IsAdmin(), addonChoiceId);
            return NoContent();
        });
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (CatalogValidationException exception)
        {
            var fields = exception.Errors.Count == 0
                ? "none"
                : string.Join(", ", exception.Errors.Keys);
            _logger.LogWarning(
                $"Catalog request validation failed for {Request.Method} {Request.Path}. UserId={GetUserIdOrUnknown()}. Fields={fields}. Message={exception.Message}");
            return JsonResponse(StatusCodes.Status400BadRequest, new
            {
                message = exception.Message,
                errors = exception.Errors
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(
                $"Catalog request access denied for {Request.Method} {Request.Path}. UserId={GetUserIdOrUnknown()}. Message={exception.Message}");
            return Forbid();
        }
        catch (KeyNotFoundException exception)
        {
            _logger.LogWarning(
                $"Catalog request resource not found for {Request.Method} {Request.Path}. UserId={GetUserIdOrUnknown()}. Message={exception.Message}");
            return JsonResponse(StatusCodes.Status404NotFound, new { message = exception.Message });
        }
    }

    private IActionResult JsonCreated(string location, object value)
    {
        Response.Headers.Location = location;
        return JsonResponse(StatusCodes.Status201Created, value);
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

    private string GetUserIdOrUnknown()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
    }
}
