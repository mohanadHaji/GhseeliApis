using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

/// <summary>
/// Repository interface for Company entity database operations
/// </summary>
public interface ICompanyRepository
{
    /// <summary>
    /// Gets all companies from the database
    /// </summary>
    Task<List<Company>> GetAllAsync();

    /// <summary>
    /// Gets a company by ID from the database
    /// </summary>
    Task<Company?> GetByIdAsync(Guid id);

    /// <summary>
    /// Adds a new company to the database
    /// </summary>
    Task<Company> AddAsync(Company company);

    /// <summary>
    /// Updates an existing company in the database
    /// </summary>
    Task<Company> UpdateAsync(Company company);

    /// <summary>
    /// Deletes a company from the database
    /// </summary>
    Task DeleteAsync(Company company);

    /// <summary>
    /// Checks if a company exists by ID
    /// </summary>
    Task<bool> ExistsAsync(Guid id);

    /// <summary>
    /// Gets companies by service area
    /// </summary>
    Task<List<Company>> GetByServiceAreaAsync(string area);
}
