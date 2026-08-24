using FluentValidation;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Companies;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/business/company")]
[Authorize(Policy = BusinessPolicies.BusinessMember)]
public class CompanyProfileController : ControllerBase
{
    private static readonly JsonSerializerOptions ResponseJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly ICompanyProfileService _companyService;

    public CompanyProfileController(ICompanyProfileService companyService)
    {
        _companyService = companyService;
    }

    [HttpGet]
    public Task<IActionResult> GetMyCompany()
    {
        return ExecuteAsync(async () => JsonResponse(
            StatusCodes.Status200OK,
            await _companyService.GetMyCompanyAsync(GetUserId())));
    }

    [HttpPut]
    [Authorize(Policy = BusinessPolicies.OwnerOrAdmin)]
    public Task<IActionResult> UpdateMyCompany(UpdateCompanyProfileRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(
            StatusCodes.Status200OK,
            await _companyService.UpdateMyCompanyAsync(GetUserId(), request)));
    }

    [HttpPost("branches")]
    [Authorize(Policy = BusinessPolicies.OwnerOrAdmin)]
    public Task<IActionResult> CreateBranch(CreateBranchRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(
            StatusCodes.Status201Created,
            await _companyService.CreateBranchAsync(GetUserId(), request)));
    }

    [HttpPut("branches/{branchId:guid}")]
    [Authorize(Policy = BusinessPolicies.OwnerOrAdmin)]
    public Task<IActionResult> UpdateBranch(
        Guid branchId,
        UpdateBranchRequest request)
    {
        return ExecuteAsync(async () => JsonResponse(
            StatusCodes.Status200OK,
            await _companyService.UpdateBranchAsync(GetUserId(), branchId, request)));
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ValidationException exception)
        {
            return JsonResponse(StatusCodes.Status400BadRequest, new
            {
                message = exception.Message,
                errors = exception.Errors
                    .GroupBy(error => ToCamelCase(error.PropertyName))
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                        StringComparer.Ordinal)
            });
        }
        catch (BusinessConflictException exception)
        {
            return JsonResponse(StatusCodes.Status409Conflict, new
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

    private static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return propertyName;
        }

        return char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
    }
}
