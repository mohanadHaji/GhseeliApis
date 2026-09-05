using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

public sealed class CustomerConfigurationRepository : ICustomerConfigurationRepository
{
    private readonly ApplicationDbContext _context;

    public CustomerConfigurationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<CustomerConfiguration?> GetActiveAsync(CancellationToken cancellationToken) =>
        _context.CustomerConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(configuration => configuration.IsActive, cancellationToken);
}
