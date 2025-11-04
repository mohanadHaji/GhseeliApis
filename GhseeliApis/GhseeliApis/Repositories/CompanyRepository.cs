using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

/// <summary>
/// Repository implementation for Company entity database operations
/// </summary>
public class CompanyRepository : ICompanyRepository
{
    private readonly ApplicationDbContext _context;

    public CompanyRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Company>> GetAllAsync()
    {
        return await _context.Companies
            .Include(c => c.Availabilities)
            .Include(c => c.ServiceOptions)
            .ToListAsync();
    }

    public async Task<Company?> GetByIdAsync(Guid id)
    {
        return await _context.Companies
            .Include(c => c.Availabilities)
            .Include(c => c.ServiceOptions)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Company> AddAsync(Company company)
    {
        _context.Companies.Add(company);
        await _context.SaveChangesAsync();
        return company;
    }

    public async Task<Company> UpdateAsync(Company company)
    {
        _context.Companies.Update(company);
        await _context.SaveChangesAsync();
        return company;
    }

    public async Task DeleteAsync(Company company)
    {
        _context.Companies.Remove(company);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.Companies.AnyAsync(c => c.Id == id);
    }

    public async Task<List<Company>> GetByServiceAreaAsync(string area)
    {
        return await _context.Companies
            .Where(c => c.ServiceAreaDescription != null && 
                       c.ServiceAreaDescription.Contains(area))
            .Include(c => c.Availabilities)
            .Include(c => c.ServiceOptions)
            .ToListAsync();
    }
}
