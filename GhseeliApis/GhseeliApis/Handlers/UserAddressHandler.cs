using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Handlers;

public class UserAddressHandler : IUserAddressHandler
{
    private readonly IUserAddressRepository _repository;
    private readonly IAppLogger _logger;

    public UserAddressHandler(IUserAddressRepository repository, IAppLogger logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<UserAddress>> GetByUserIdAsync(Guid userId)
    {
        try
        {
            _logger.LogInfo($"UserAddressHandler: Getting addresses for user {userId}");
            return await _repository.GetByUserIdAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"UserAddressHandler: Error getting addresses for user {userId}", ex);
            throw;
        }
    }

    public async Task<UserAddress?> GetByIdAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"UserAddressHandler: Getting address {id}");
            return await _repository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError($"UserAddressHandler: Error getting address {id}", ex);
            throw;
        }
    }

    public async Task<UserAddress?> GetPrimaryAddressAsync(Guid userId)
    {
        try
        {
            _logger.LogInfo($"UserAddressHandler: Getting primary address for user {userId}");
            return await _repository.GetPrimaryAddressAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"UserAddressHandler: Error getting primary address for user {userId}", ex);
            throw;
        }
    }

    public async Task<UserAddress> CreateAsync(UserAddress address, Guid userId)
    {
        try
        {
            _logger.LogInfo($"UserAddressHandler: Creating address for user {userId}");
            address.UserId = userId;
            address.Id = Guid.NewGuid();
            return await _repository.AddAsync(address);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError("UserAddressHandler: Database error creating address", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("UserAddressHandler: Error creating address", ex);
            throw;
        }
    }

    public async Task<UserAddress?> UpdateAsync(Guid id, UserAddress address, Guid userId)
    {
        try
        {
            _logger.LogInfo($"UserAddressHandler: Updating address {id}");
            
            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning($"UserAddressHandler: Address {id} not found");
                return null;
            }

            if (existing.UserId != userId)
            {
                _logger.LogWarning($"UserAddressHandler: User {userId} does not own address {id}");
                return null;
            }

            existing.AddressLine = address.AddressLine;
            existing.City = address.City;
            existing.Area = address.Area;
            existing.Latitude = address.Latitude;
            existing.Longitude = address.Longitude;
            existing.IsPrimary = address.IsPrimary;

            return await _repository.UpdateAsync(existing);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError($"UserAddressHandler: Database error updating address {id}", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"UserAddressHandler: Error updating address {id}", ex);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId)
    {
        try
        {
            _logger.LogInfo($"UserAddressHandler: Deleting address {id}");
            
            var address = await _repository.GetByIdAsync(id);
            if (address == null)
            {
                _logger.LogWarning($"UserAddressHandler: Address {id} not found");
                return false;
            }

            if (address.UserId != userId)
            {
                _logger.LogWarning($"UserAddressHandler: User {userId} does not own address {id}");
                return false;
            }

            if (await _repository.HasActiveBookingsAsync(id))
            {
                _logger.LogWarning($"UserAddressHandler: Cannot delete address {id} - has active bookings");
                throw new InvalidOperationException("Cannot delete address with active bookings");
            }

            await _repository.DeleteAsync(address);
            _logger.LogInfo($"UserAddressHandler: Address {id} deleted successfully");
            return true;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"UserAddressHandler: Error deleting address {id}", ex);
            throw;
        }
    }

    public async Task<bool> SetAsPrimaryAsync(Guid id, Guid userId)
    {
        try
        {
            _logger.LogInfo($"UserAddressHandler: Setting address {id} as primary for user {userId}");
            
            var address = await _repository.GetByIdAsync(id);
            if (address == null || address.UserId != userId)
            {
                _logger.LogWarning($"UserAddressHandler: Address {id} not found or access denied");
                return false;
            }

            await _repository.SetAsPrimaryAsync(id, userId);
            _logger.LogInfo($"UserAddressHandler: Address {id} set as primary successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"UserAddressHandler: Error setting address {id} as primary", ex);
            throw;
        }
    }
}
