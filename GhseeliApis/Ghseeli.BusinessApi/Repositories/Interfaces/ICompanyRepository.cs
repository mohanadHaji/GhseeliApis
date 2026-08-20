using Ghseeli.BusinessApi.Models;

namespace Ghseeli.BusinessApi.Repositories.Interfaces;

public interface ICompanyRepository
{
    Task CreateForOwnerAsync(Company company, BusinessUserAssignment assignment);
    Task<Company?> GetByIdAsync(Guid companyId);
    Task<Company?> GetPublicationByIdAsync(Guid companyId);
    Task<Company?> GetForUserAsync(Guid userId);
    Task<BusinessUserAssignment?> GetAssignmentForUserAsync(Guid userId);
    Task<Company> UpdateAsync(Company company);
    Task<Branch> AddBranchAsync(Branch branch);
    Task<Branch?> GetBranchByIdAsync(Guid branchId);
    Task<Branch?> GetBranchForUserAsync(Guid userId, Guid branchId);
    Task<Branch> UpdateBranchAsync(Branch branch);
}
