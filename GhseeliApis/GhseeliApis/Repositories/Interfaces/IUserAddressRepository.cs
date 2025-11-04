using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

/// <summary>
/// Repository interface for UserAddress entity database operations
/// </summary>
public interface IUserAddressRepository
{
    /// <summary>
    /// Gets all addresses from the database
    /// </summary>
    Task<List<UserAddress>> GetAllAsync();

    /// <summary>
    /// Gets addresses for a specific user
    /// </summary>
    Task<List<UserAddress>> GetByUserIdAsync(Guid userId);

    /// <summary>
    /// Gets an address by ID from the database
    /// </summary>
    Task<UserAddress?> GetByIdAsync(Guid id);

    /// <summary>
    /// Gets the primary address for a user
    /// </summary>
    Task<UserAddress?> GetPrimaryAddressAsync(Guid userId);

    /// <summary>
    /// Adds a new address to the database
    /// </summary>
    Task<UserAddress> AddAsync(UserAddress address);

    /// <summary>
    /// Updates an existing address in the database
    /// </summary>
    Task<UserAddress> UpdateAsync(UserAddress address);

    /// <summary>
    /// Deletes an address from the database
    /// </summary>
    Task DeleteAsync(UserAddress address);

    /// <summary>
    /// Checks if an address exists by ID
    /// </summary>
    Task<bool> ExistsAsync(Guid id);

    /// <summary>
    /// Sets an address as primary (and unsets others)
    /// </summary>
    Task SetAsPrimaryAsync(Guid addressId, Guid userId);

    /// <summary>
    /// Checks if an address has active bookings
    /// </summary>
    Task<bool> HasActiveBookingsAsync(Guid addressId);
}
