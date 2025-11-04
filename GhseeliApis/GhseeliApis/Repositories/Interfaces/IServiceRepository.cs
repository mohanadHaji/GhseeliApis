using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

/// <summary>
/// Repository interface for Service entity database operations
/// </summary>
public interface IServiceRepository
{
    /// <summary>
    /// Gets all services from the database
    /// </summary>
    Task<List<Service>> GetAllAsync();

    /// <summary>
    /// Gets a service by ID from the database
    /// </summary>
    Task<Service?> GetByIdAsync(Guid id);

    /// <summary>
    /// Gets a service with its options
    /// </summary>
    Task<Service?> GetByIdWithOptionsAsync(Guid id);

    /// <summary>
    /// Adds a new service to the database
    /// </summary>
    Task<Service> AddAsync(Service service);

    /// <summary>
    /// Updates an existing service in the database
    /// </summary>
    Task<Service> UpdateAsync(Service service);

    /// <summary>
    /// Deletes a service from the database
    /// </summary>
    Task DeleteAsync(Service service);

    /// <summary>
    /// Checks if a service exists by ID
    /// </summary>
    Task<bool> ExistsAsync(Guid id);

    /// <summary>
    /// Gets a service by name
    /// </summary>
    Task<Service?> GetByNameAsync(string name);
}
