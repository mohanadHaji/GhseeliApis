namespace GhseeliApis.Repositories.Interfaces;

/// <summary>
/// Repository interface for database health check operations
/// </summary>
public interface IHealthRepository
{
    /// <summary>
    /// Checks if the database connection is healthy
    /// </summary>
    /// <returns>True if database is accessible, false otherwise</returns>
    Task<bool> CanConnectAsync();

    /// <summary>
    /// Gets the count of users in the database (for health verification)
    /// </summary>
    /// <returns>Number of users in the database</returns>
    Task<int> GetUserCountAsync();

    /// <summary>
    /// Verifies a Customer-owned table and the expected migration state.
    /// </summary>
    Task<bool> CanQueryOwnedSchemaAsync();
}
