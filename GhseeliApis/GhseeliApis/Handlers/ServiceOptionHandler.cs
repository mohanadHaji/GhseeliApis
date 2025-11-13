using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Handlers;

public class ServiceOptionHandler : IServiceOptionHandler
{
    private readonly IServiceOptionRepository _repository;
    private readonly IAppLogger _logger;

    public ServiceOptionHandler(IServiceOptionRepository repository, IAppLogger logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<ServiceOption>> GetAllAsync()
    {
        try
        {
            _logger.LogInfo("ServiceOptionHandler: Getting all service options");
            return await _repository.GetAllAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("ServiceOptionHandler: Error getting all service options", ex);
            throw;
        }
    }

    public async Task<List<ServiceOption>> GetByServiceIdAsync(Guid serviceId)
    {
        try
        {
            _logger.LogInfo($"ServiceOptionHandler: Getting service options for service {serviceId}");
            return await _repository.GetByServiceIdAsync(serviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ServiceOptionHandler: Error getting service options for service {serviceId}", ex);
            throw;
        }
    }

    public async Task<List<ServiceOption>> GetByCompanyIdAsync(Guid companyId)
    {
        try
        {
            _logger.LogInfo($"ServiceOptionHandler: Getting service options for company {companyId}");
            return await _repository.GetByCompanyIdAsync(companyId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ServiceOptionHandler: Error getting service options for company {companyId}", ex);
            throw;
        }
    }

    public async Task<ServiceOption?> GetByIdAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"ServiceOptionHandler: Getting service option {id}");
            return await _repository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ServiceOptionHandler: Error getting service option {id}", ex);
            throw;
        }
    }

    public async Task<ServiceOption> CreateAsync(ServiceOption serviceOption)
    {
        try
        {
            _logger.LogInfo($"ServiceOptionHandler: Creating service option '{serviceOption.Name}'");

            serviceOption.Id = Guid.NewGuid();

            // Validate the service option
            var validationResult = serviceOption.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"ServiceOptionHandler: Validation failed: {string.Join(", ", validationResult.Errors)}");
                throw new InvalidOperationException($"Validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            var created = await _repository.AddAsync(serviceOption);
            
            _logger.LogInfo($"ServiceOptionHandler: Service option '{created.Name}' created successfully with ID {created.Id}");
            
            return created;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError("ServiceOptionHandler: Database error creating service option", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("ServiceOptionHandler: Error creating service option", ex);
            throw;
        }
    }

    public async Task<ServiceOption?> UpdateAsync(Guid id, ServiceOption serviceOption)
    {
        try
        {
            _logger.LogInfo($"ServiceOptionHandler: Updating service option {id}");

            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning($"ServiceOptionHandler: Service option {id} not found");
                return null;
            }

            existing.Name = serviceOption.Name;
            existing.Description = serviceOption.Description;
            existing.DurationMinutes = serviceOption.DurationMinutes;
            existing.Price = serviceOption.Price;

            // Validate the updated service option
            var validationResult = existing.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"ServiceOptionHandler: Validation failed: {string.Join(", ", validationResult.Errors)}");
                throw new InvalidOperationException($"Validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            var updated = await _repository.UpdateAsync(existing);
            
            _logger.LogInfo($"ServiceOptionHandler: Service option {id} updated successfully");
            
            return updated;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError($"ServiceOptionHandler: Database error updating service option {id}", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"ServiceOptionHandler: Error updating service option {id}", ex);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"ServiceOptionHandler: Deleting service option {id}");

            var serviceOption = await _repository.GetByIdAsync(id);
            if (serviceOption == null)
            {
                _logger.LogWarning($"ServiceOptionHandler: Service option {id} not found");
                return false;
            }

            await _repository.DeleteAsync(serviceOption);
            
            _logger.LogInfo($"ServiceOptionHandler: Service option {id} deleted successfully");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"ServiceOptionHandler: Error deleting service option {id}", ex);
            throw;
        }
    }
}
