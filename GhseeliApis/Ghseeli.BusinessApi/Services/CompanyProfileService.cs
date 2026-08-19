using Ghseeli.BusinessApi.DTOs.Companies;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services.Interfaces;

namespace Ghseeli.BusinessApi.Services;

public class CompanyProfileService : ICompanyProfileService
{
    private readonly ICompanyRepository _repository;

    public CompanyProfileService(ICompanyRepository repository)
    {
        _repository = repository;
    }

    public async Task<CompanyProfileResponse> GetMyCompanyAsync(Guid userId)
    {
        var company = await GetAssignedCompanyAsync(userId);
        return MapCompany(company);
    }

    public async Task<CompanyProfileResponse> UpdateMyCompanyAsync(
        Guid userId,
        UpdateCompanyProfileRequest request)
    {
        var company = await GetAssignedCompanyAsync(userId);
        company.NameAr = request.NameAr.Trim();
        company.NameHe = request.NameHe.Trim();
        company.DescriptionAr = request.DescriptionAr;
        company.DescriptionHe = request.DescriptionHe;
        company.ServiceAreaDescriptionAr = request.ServiceAreaDescriptionAr;
        company.ServiceAreaDescriptionHe = request.ServiceAreaDescriptionHe;
        company.Phone = request.Phone;
        company.UpdatedAt = DateTime.UtcNow;

        return MapCompany(await _repository.UpdateAsync(company));
    }

    public async Task<BranchResponse> CreateBranchAsync(
        Guid userId,
        CreateBranchRequest request)
    {
        var company = await GetAssignedCompanyAsync(userId);
        var branch = new Branch
        {
            CompanyId = company.Id,
            NameAr = request.NameAr.Trim(),
            NameHe = request.NameHe.Trim(),
            AddressAr = request.AddressAr.Trim(),
            AddressHe = request.AddressHe.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        return MapBranch(await _repository.AddBranchAsync(branch));
    }

    public async Task<BranchResponse> UpdateBranchAsync(
        Guid userId,
        Guid branchId,
        UpdateBranchRequest request)
    {
        var branch = await _repository.GetBranchForUserAsync(userId, branchId)
            ?? throw new UnauthorizedAccessException(
                "The branch is not assigned to this business account.");

        branch.NameAr = request.NameAr.Trim();
        branch.NameHe = request.NameHe.Trim();
        branch.AddressAr = request.AddressAr.Trim();
        branch.AddressHe = request.AddressHe.Trim();
        branch.Latitude = request.Latitude;
        branch.Longitude = request.Longitude;
        branch.IsActive = request.IsActive;
        branch.UpdatedAt = DateTime.UtcNow;

        return MapBranch(await _repository.UpdateBranchAsync(branch));
    }

    private async Task<Company> GetAssignedCompanyAsync(Guid userId)
    {
        return await _repository.GetForUserAsync(userId)
            ?? throw new UnauthorizedAccessException(
                "No active company assignment was found for this business account.");
    }

    private static CompanyProfileResponse MapCompany(Company company)
    {
        return new CompanyProfileResponse
        {
            Id = company.Id,
            NameAr = company.NameAr,
            NameHe = company.NameHe,
            DescriptionAr = company.DescriptionAr,
            DescriptionHe = company.DescriptionHe,
            ServiceAreaDescriptionAr = company.ServiceAreaDescriptionAr,
            ServiceAreaDescriptionHe = company.ServiceAreaDescriptionHe,
            Phone = company.Phone,
            IsActive = company.IsActive,
            Branches = company.Branches.Select(MapBranch).ToArray()
        };
    }

    private static BranchResponse MapBranch(Branch branch)
    {
        return new BranchResponse
        {
            Id = branch.Id,
            NameAr = branch.NameAr,
            NameHe = branch.NameHe,
            AddressAr = branch.AddressAr,
            AddressHe = branch.AddressHe,
            Latitude = branch.Latitude,
            Longitude = branch.Longitude,
            IsActive = branch.IsActive
        };
    }
}
