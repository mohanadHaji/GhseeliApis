using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Repositories;

public class CompanyRepository : ICompanyRepository
{
    private readonly BusinessDbContext _context;

    public CompanyRepository(BusinessDbContext context)
    {
        _context = context;
    }

    public async Task CreateForOwnerAsync(
        Company company,
        BusinessUserAssignment assignment)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        _context.Companies.Add(company);
        _context.BusinessUserAssignments.Add(assignment);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<Company?> GetForUserAsync(Guid userId)
    {
        return await _context.Companies
            .Include(company => company.Branches)
            .SingleOrDefaultAsync(company =>
                company.Assignments.Any(assignment =>
                    assignment.UserId == userId && assignment.IsActive));
    }

    public Task<BusinessUserAssignment?> GetAssignmentForUserAsync(Guid userId)
    {
        return _context.BusinessUserAssignments
            .Include(assignment => assignment.Company)
            .SingleOrDefaultAsync(assignment =>
                assignment.UserId == userId && assignment.IsActive);
    }

    public async Task<Company> UpdateAsync(Company company)
    {
        _context.Companies.Update(company);
        await _context.SaveChangesAsync();
        return company;
    }

    public async Task<Branch> AddBranchAsync(Branch branch)
    {
        _context.Branches.Add(branch);
        await _context.SaveChangesAsync();
        return branch;
    }

    public Task<Branch?> GetBranchForUserAsync(Guid userId, Guid branchId)
    {
        return _context.Branches.SingleOrDefaultAsync(branch =>
            branch.Id == branchId &&
            branch.Company.Assignments.Any(assignment =>
                assignment.UserId == userId && assignment.IsActive));
    }

    public async Task<Branch> UpdateBranchAsync(Branch branch)
    {
        _context.Branches.Update(branch);
        await _context.SaveChangesAsync();
        return branch;
    }
}
