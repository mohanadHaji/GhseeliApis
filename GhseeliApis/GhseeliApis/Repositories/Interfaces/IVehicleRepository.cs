using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

/// <summary>
/// Repository interface for Vehicle entity database operations
/// </summary>
public interface IVehicleRepository
{
    /// <summary>
    /// Gets all vehicles from the database
    /// </summary>
    Task<List<Vehicle>> GetAllAsync();

    /// <summary>
    /// Gets vehicles for a specific user
    /// </summary>
    Task<List<Vehicle>> GetByUserIdAsync(Guid userId);

    /// <summary>
    /// Gets a vehicle by ID from the database
    /// </summary>
    Task<Vehicle?> GetByIdAsync(Guid id);

    /// <summary>
    /// Adds a new vehicle to the database
    /// </summary>
    Task<Vehicle> AddAsync(Vehicle vehicle);

    /// <summary>
    /// Updates an existing vehicle in the database
    /// </summary>
    Task<Vehicle> UpdateAsync(Vehicle vehicle);

    /// <summary>
    /// Deletes a vehicle from the database
    /// </summary>
    Task DeleteAsync(Vehicle vehicle);

    /// <summary>
    /// Checks if a vehicle exists by ID
    /// </summary>
    Task<bool> ExistsAsync(Guid id);

    /// <summary>
    /// Checks if a vehicle has active bookings
    /// </summary>
    Task<bool> HasActiveBookingsAsync(Guid vehicleId);

    /// <summary>
    /// Gets a vehicle by license plate
    /// </summary>
    Task<Vehicle?> GetByLicensePlateAsync(string licensePlate);
}
