using GhseeliApis.Data;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Handlers;

/// <summary>
/// Handler for health check operations
/// </summary>
public class HealthHandler : IHealthHandler
{
    private readonly ApplicationDbContext _db;
    private readonly IAppLogger _logger;

    public HealthHandler(ApplicationDbContext db, IAppLogger logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Checks if the database connection is healthy
    /// </summary>
    public async Task<bool> CheckDatabaseHealthAsync()
    {
        try
        {
            _logger.LogInfo("CheckDatabaseHealthAsync: Starting database health check...");

            var startTime = DateTime.UtcNow;
            var canConnect = await _db.Database.CanConnectAsync();
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            if (canConnect)
            {
                _logger.LogInfo($"CheckDatabaseHealthAsync: Database connection healthy - Response time: {duration:F2}ms");

                // Check if we can query the database
                try
                {
                    var userCount = await _db.Users.CountAsync();
                    _logger.LogInfo($"CheckDatabaseHealthAsync: Database query successful - Current user count: {userCount}");
                }
                catch (Exception queryEx)
                {
                    _logger.LogWarning($"CheckDatabaseHealthAsync: Database connected but query failed - Connection may be limited - Error: {queryEx.Message}");
                }
            }
            else
            {
                _logger.LogWarning($"CheckDatabaseHealthAsync: Database connection failed - Response time: {duration:F2}ms");
            }

            return canConnect;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError("CheckDatabaseHealthAsync: Invalid operation - Database context may be disposed or misconfigured", ex);
            return false;
        }
        catch (TimeoutException ex)
        {
            _logger.LogError("CheckDatabaseHealthAsync: Database connection timeout - Server may be unreachable or overloaded", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError("CheckDatabaseHealthAsync: Unexpected error during database health check", ex);
            return false;
        }
    }
}
