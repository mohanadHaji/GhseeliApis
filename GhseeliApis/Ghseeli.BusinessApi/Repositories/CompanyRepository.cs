using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Repositories;

public class CompanyRepository : BusinessMutationRepositoryBase, ICompanyRepository
{
    private const string ConflictMessage =
        "The requested business company change conflicted with a newer authoritative update. Reload the latest data and retry.";

    public CompanyRepository(BusinessDbContext context)
        : base(context)
    {
    }

    public async Task CreateForOwnerAsync(
        Company company,
        BusinessUserAssignment assignment)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync();
        Context.Companies.Add(company);
        Context.BusinessUserAssignments.Add(assignment);
        await Context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public Task<Company?> GetByIdAsync(Guid companyId)
    {
        return Context.Companies
            .Include(company => company.Branches)
            .SingleOrDefaultAsync(company => company.Id == companyId);
    }

    public Task<Company?> GetPublicationByIdAsync(Guid companyId)
    {
        return Context.Companies
            .Include(company => company.Branches)
                .ThenInclude(branch => branch.ServiceArea)
            .Include(company => company.Categories)
                .ThenInclude(category => category.Offerings)
                    .ThenInclude(offering => offering.Branch)
            .Include(company => company.Categories)
                .ThenInclude(category => category.Offerings)
                    .ThenInclude(offering => offering.AddonGroups)
                        .ThenInclude(group => group.Choices)
            .SingleOrDefaultAsync(company => company.Id == companyId);
    }

    public async Task<Company?> GetForUserAsync(Guid userId)
    {
        return await Context.Companies
            .Include(company => company.Branches)
            .SingleOrDefaultAsync(company =>
                company.Assignments.Any(assignment =>
                    assignment.UserId == userId && assignment.IsActive));
    }

    public Task<BusinessUserAssignment?> GetAssignmentForUserAsync(Guid userId)
    {
        return Context.BusinessUserAssignments
            .Include(assignment => assignment.Company)
            .SingleOrDefaultAsync(assignment =>
                assignment.UserId == userId && assignment.IsActive);
    }

    public async Task<Company> UpdateAsync(Company company)
    {
        Context.Companies.Update(company);
        await PrepareCompanyVersionIncrementAsync(company.Id);
        await PersistMutationAsync(
            company.Id,
            ConflictMessage,
            allowCompanyOnlyRetry: true,
            allowCompanyProfileRetry: true);
        return company;
    }

    public async Task<Branch> AddBranchAsync(Branch branch)
    {
        Context.Branches.Add(branch);
        await PrepareCompanyVersionIncrementAsync(branch.CompanyId);
        await PersistMutationAsync(
            branch.CompanyId,
            ConflictMessage,
            allowCompanyOnlyRetry: true);
        return branch;
    }

    public Task<Branch?> GetBranchByIdAsync(Guid branchId)
    {
        return Context.Branches
            .Include(branch => branch.Company)
            .Include(branch => branch.ServiceArea)
            .SingleOrDefaultAsync(branch => branch.Id == branchId);
    }

    public Task<Branch?> GetBranchForUserAsync(Guid userId, Guid branchId)
    {
        return Context.Branches
            .Include(branch => branch.Company)
            .Include(branch => branch.ServiceArea)
            .SingleOrDefaultAsync(branch =>
                branch.Id == branchId &&
                branch.Company.Assignments.Any(assignment =>
                    assignment.UserId == userId && assignment.IsActive));
    }

    public async Task<Branch> UpdateBranchAsync(Branch branch)
    {
        Context.Branches.Update(branch);
        await PrepareCompanyVersionIncrementAsync(branch.CompanyId);
        await PersistMutationAsync(
            branch.CompanyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        return branch;
    }
}
