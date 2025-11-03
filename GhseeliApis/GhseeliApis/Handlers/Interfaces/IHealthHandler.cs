namespace GhseeliApis.Handlers.Interfaces;

/// <summary>
/// Interface for health check operations
/// </summary>
public interface IHealthHandler
{
    /// <summary>
    /// Checks if the database connection is healthy
    /// </summary>
    /// <returns>True if database is accessible, false otherwise</returns>
    Task<bool> CheckDatabaseHealthAsync();
}
