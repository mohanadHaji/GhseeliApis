using GhseeliApis.Models;

namespace GhseeliApis.Handlers.Interfaces;

/// <summary>
/// Interface for company-related business logic
/// </summary>
public interface ICompanyHandler
{
    /// <summary>
    /// Gets all companies
    /// </summary>
    Task<List<Company>> GetAllAsync();

    /// <summary>
    /// Gets a company by ID
    /// </summary>
    Task<Company?> GetByIdAsync(Guid id);

    /// <summary>
    /// Gets companies by service area
    /// </summary>
    Task<List<Company>> GetByServiceAreaAsync(string area);

    /// <summary>
    /// Creates a new company
    /// </summary>
    Task<Company> CreateAsync(Company company);

    /// <summary>
    /// Updates a company
    /// </summary>
    Task<Company?> UpdateAsync(Guid id, Company company);

    /// <summary>
    /// Deletes a company
    /// </summary>
    Task<bool> DeleteAsync(Guid id);
}
