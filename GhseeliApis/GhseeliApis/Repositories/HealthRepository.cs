using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

/// <summary>
/// Repository implementation for database health check operations
/// </summary>
public class HealthRepository : IHealthRepository
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
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
            using var timeout = new CancellationTokenSource(ProbeTimeout);
            return await _context.Database.CanConnectAsync(timeout.Token);
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
        using var timeout = new CancellationTokenSource(ProbeTimeout);
        return await _context.Users.CountAsync(timeout.Token);
    }

    public async Task<bool> CanQueryOwnedSchemaAsync()
    {
        using var timeout = new CancellationTokenSource(ProbeTimeout);
        _ = await _context.CustomerDevices.AsNoTracking().AnyAsync(timeout.Token);
        if (!_context.Database.IsRelational())
        {
            return true;
        }
        var expected = _context.Database.GetMigrations().ToArray();
        var applied = (await _context.Database.GetAppliedMigrationsAsync(timeout.Token)).ToArray();
        var pending = (await _context.Database.GetPendingMigrationsAsync(timeout.Token)).ToArray();
        return expected.SequenceEqual(applied, StringComparer.Ordinal) &&
               pending.Length == 0;
    }
}
