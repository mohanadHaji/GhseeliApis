using GhseeliApis.Models;

namespace GhseeliApis.Handlers.Interfaces;

public interface IVehicleHandler
{
    Task<List<Vehicle>> GetAllAsync();
    Task<List<Vehicle>> GetByUserIdAsync(Guid userId);
    Task<Vehicle?> GetByIdAsync(Guid id);
    Task<Vehicle> CreateAsync(Vehicle vehicle, Guid userId);
    Task<Vehicle?> UpdateAsync(Guid id, Vehicle vehicle, Guid userId);
    Task<bool> DeleteAsync(Guid id, Guid userId);
    Task<bool> CanDeleteAsync(Guid id);
}
