using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Companies;
using Ghseeli.BusinessApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/business/company")]
[Authorize(Policy = BusinessPolicies.BusinessMember)]
public class CompanyProfileController : ControllerBase
{
    private readonly ICompanyProfileService _companyService;

    public CompanyProfileController(ICompanyProfileService companyService)
    {
        _companyService = companyService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyCompany()
    {
        return Ok(await _companyService.GetMyCompanyAsync(GetUserId()));
    }

    [HttpPut]
    [Authorize(Policy = BusinessPolicies.OwnerOrAdmin)]
    public async Task<IActionResult> UpdateMyCompany(UpdateCompanyProfileRequest request)
    {
        return Ok(await _companyService.UpdateMyCompanyAsync(GetUserId(), request));
    }

    [HttpPost("branches")]
    [Authorize(Policy = BusinessPolicies.OwnerOrAdmin)]
    public async Task<IActionResult> CreateBranch(CreateBranchRequest request)
    {
        return Ok(await _companyService.CreateBranchAsync(GetUserId(), request));
    }

    [HttpPut("branches/{branchId:guid}")]
    [Authorize(Policy = BusinessPolicies.OwnerOrAdmin)]
    public async Task<IActionResult> UpdateBranch(
        Guid branchId,
        UpdateBranchRequest request)
    {
        try
        {
            return Ok(await _companyService.UpdateBranchAsync(
                GetUserId(), branchId, request));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private Guid GetUserId()
    {
        return Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
