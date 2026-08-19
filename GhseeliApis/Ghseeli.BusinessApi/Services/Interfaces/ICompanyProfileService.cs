using Ghseeli.BusinessApi.DTOs.Companies;

namespace Ghseeli.BusinessApi.Services.Interfaces;

public interface ICompanyProfileService
{
    Task<CompanyProfileResponse> GetMyCompanyAsync(Guid userId);
    Task<CompanyProfileResponse> UpdateMyCompanyAsync(
        Guid userId,
        UpdateCompanyProfileRequest request);
    Task<BranchResponse> CreateBranchAsync(Guid userId, CreateBranchRequest request);
    Task<BranchResponse> UpdateBranchAsync(
        Guid userId,
        Guid branchId,
        UpdateBranchRequest request);
}
