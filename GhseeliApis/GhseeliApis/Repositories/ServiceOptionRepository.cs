using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

/// <summary>
/// Repository implementation for ServiceOption entity database operations
/// </summary>
public class ServiceOptionRepository : IServiceOptionRepository
{
    private readonly ApplicationDbContext _context;

    public ServiceOptionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ServiceOption>> GetAllAsync()
    {
        return await _context.ServiceOptions
            .Include(so => so.Service)
            .Include(so => so.Company)
            .ToListAsync();
    }

    public async Task<List<ServiceOption>> GetByServiceIdAsync(Guid serviceId)
    {
        return await _context.ServiceOptions
            .Where(so => so.ServiceId == serviceId)
            .Include(so => so.Company)
            .ToListAsync();
    }

    public async Task<List<ServiceOption>> GetByCompanyIdAsync(Guid companyId)
    {
        return await _context.ServiceOptions
            .Where(so => so.CompanyId == companyId)
            .Include(so => so.Service)
            .ToListAsync();
    }

    public async Task<ServiceOption?> GetByIdAsync(Guid id)
    {
        return await _context.ServiceOptions
            .Include(so => so.Service)
            .Include(so => so.Company)
            .FirstOrDefaultAsync(so => so.Id == id);
    }

    public async Task<ServiceOption> AddAsync(ServiceOption serviceOption)
    {
        _context.ServiceOptions.Add(serviceOption);
        await _context.SaveChangesAsync();
        return serviceOption;
    }

    public async Task<ServiceOption> UpdateAsync(ServiceOption serviceOption)
    {
        _context.ServiceOptions.Update(serviceOption);
        await _context.SaveChangesAsync();
        return serviceOption;
    }

    public async Task DeleteAsync(ServiceOption serviceOption)
    {
        _context.ServiceOptions.Remove(serviceOption);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.ServiceOptions.AnyAsync(so => so.Id == id);
    }
}
