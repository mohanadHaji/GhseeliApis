using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

/// <summary>
/// Repository interface for ServiceOption entity database operations
/// </summary>
public interface IServiceOptionRepository
{
    /// <summary>
    /// Gets all service options from the database
    /// </summary>
    Task<List<ServiceOption>> GetAllAsync();

    /// <summary>
    /// Gets service options for a specific service
    /// </summary>
    Task<List<ServiceOption>> GetByServiceIdAsync(Guid serviceId);

    /// <summary>
    /// Gets service options for a specific company
    /// </summary>
    Task<List<ServiceOption>> GetByCompanyIdAsync(Guid companyId);

    /// <summary>
    /// Gets a service option by ID from the database
    /// </summary>
    Task<ServiceOption?> GetByIdAsync(Guid id);

    /// <summary>
    /// Adds a new service option to the database
    /// </summary>
    Task<ServiceOption> AddAsync(ServiceOption serviceOption);

    /// <summary>
    /// Updates an existing service option in the database
    /// </summary>
    Task<ServiceOption> UpdateAsync(ServiceOption serviceOption);

    /// <summary>
    /// Deletes a service option from the database
    /// </summary>
    Task DeleteAsync(ServiceOption serviceOption);

    /// <summary>
    /// Checks if a service option exists by ID
    /// </summary>
    Task<bool> ExistsAsync(Guid id);
}
