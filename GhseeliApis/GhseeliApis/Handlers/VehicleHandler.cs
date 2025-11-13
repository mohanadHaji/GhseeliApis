using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Handlers;

public class VehicleHandler : IVehicleHandler
{
    private readonly IVehicleRepository _repository;
    private readonly IAppLogger _logger;

    public VehicleHandler(IVehicleRepository repository, IAppLogger logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<Vehicle>> GetAllAsync()
    {
        try
        {
            _logger.LogInfo("VehicleHandler: Getting all vehicles");
            return await _repository.GetAllAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("VehicleHandler: Error getting all vehicles", ex);
            throw;
        }
    }

    public async Task<List<Vehicle>> GetByUserIdAsync(Guid userId)
    {
        try
        {
            _logger.LogInfo($"VehicleHandler: Getting vehicles for user {userId}");
            return await _repository.GetByUserIdAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"VehicleHandler: Error getting vehicles for user {userId}", ex);
            throw;
        }
    }

    public async Task<Vehicle?> GetByIdAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"VehicleHandler: Getting vehicle {id}");
            return await _repository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError($"VehicleHandler: Error getting vehicle {id}", ex);
            throw;
        }
    }

    public async Task<Vehicle> CreateAsync(Vehicle vehicle, Guid userId)
    {
        try
        {
            _logger.LogInfo($"VehicleHandler: Creating vehicle for user {userId}");
            vehicle.UserId = userId;
            vehicle.Id = Guid.NewGuid();

            // Validate the vehicle
            var validationResult = vehicle.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"VehicleHandler: Validation failed: {string.Join(", ", validationResult.Errors)}");
                throw new InvalidOperationException($"Validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            return await _repository.AddAsync(vehicle);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError("VehicleHandler: Database error creating vehicle", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("VehicleHandler: Error creating vehicle", ex);
            throw;
        }
    }

    public async Task<Vehicle?> UpdateAsync(Guid id, Vehicle vehicle, Guid userId)
    {
        try
        {
            _logger.LogInfo($"VehicleHandler: Updating vehicle {id}");
            
            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning($"VehicleHandler: Vehicle {id} not found");
                return null;
            }

            // Ensure vehicle belongs to user
            if (existing.UserId != userId)
            {
                _logger.LogWarning($"VehicleHandler: User {userId} does not own vehicle {id}");
                return null;
            }

            existing.Make = vehicle.Make;
            existing.Model = vehicle.Model;
            existing.Year = vehicle.Year;
            existing.LicensePlate = vehicle.LicensePlate;
            existing.Color = vehicle.Color;

            // Validate the updated vehicle
            var validationResult = existing.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"VehicleHandler: Validation failed: {string.Join(", ", validationResult.Errors)}");
                throw new InvalidOperationException($"Validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            return await _repository.UpdateAsync(existing);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError($"VehicleHandler: Database error updating vehicle {id}", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"VehicleHandler: Error updating vehicle {id}", ex);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId)
    {
        try
        {
            _logger.LogInfo($"VehicleHandler: Deleting vehicle {id}");
            
            var vehicle = await _repository.GetByIdAsync(id);
            if (vehicle == null)
            {
                _logger.LogWarning($"VehicleHandler: Vehicle {id} not found");
                return false;
            }

            // Ensure vehicle belongs to user
            if (vehicle.UserId != userId)
            {
                _logger.LogWarning($"VehicleHandler: User {userId} does not own vehicle {id}");
                return false;
            }

            // Check for active bookings
            if (await _repository.HasActiveBookingsAsync(id))
            {
                _logger.LogWarning($"VehicleHandler: Cannot delete vehicle {id} - has active bookings");
                throw new InvalidOperationException("Cannot delete vehicle with active bookings");
            }

            await _repository.DeleteAsync(vehicle);
            _logger.LogInfo($"VehicleHandler: Vehicle {id} deleted successfully");
            return true;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"VehicleHandler: Error deleting vehicle {id}", ex);
            throw;
        }
    }

    public async Task<bool> CanDeleteAsync(Guid id)
    {
        try
        {
            return !await _repository.HasActiveBookingsAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError($"VehicleHandler: Error checking if vehicle {id} can be deleted", ex);
            throw;
        }
    }
}
