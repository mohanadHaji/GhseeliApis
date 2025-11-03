using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

/// <summary>
/// Repository implementation for database health check operations
/// </summary>
public class HealthRepository : IHealthRepository
{
    private readonly ApplicationDbContext _context;

    public HealthRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Checks if the database connection is healthy
    /// </summary>
    public async Task<bool> CanConnectAsync()
    {
        try
        {
            return await _context.Database.CanConnectAsync();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the count of users in the database (for health verification)
    /// </summary>
    public async Task<int> GetUserCountAsync()
    {
        return await _context.Users.CountAsync();
    }
}
