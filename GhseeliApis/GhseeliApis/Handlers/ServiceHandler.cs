using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Handlers;

/// <summary>
/// Handler for service-related business logic
/// </summary>
public class ServiceHandler : IServiceHandler
{
    private readonly IServiceRepository _repository;
    private readonly IAppLogger _logger;

    public ServiceHandler(IServiceRepository repository, IAppLogger logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<Service>> GetAllAsync()
    {
        try
        {
            _logger.LogInfo("ServiceHandler: Getting all services");
            return await _repository.GetAllAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("ServiceHandler: Error getting all services", ex);
            throw;
        }
    }

    public async Task<Service?> GetByIdAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"ServiceHandler: Getting service {id}");
            return await _repository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ServiceHandler: Error getting service {id}", ex);
            throw;
        }
    }

    public async Task<Service?> GetByIdWithOptionsAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"ServiceHandler: Getting service {id} with options");
            return await _repository.GetByIdWithOptionsAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ServiceHandler: Error getting service {id} with options", ex);
            throw;
        }
    }

    public async Task<Service> CreateAsync(Service service)
    {
        try
        {
            _logger.LogInfo($"ServiceHandler: Creating service '{service.Name}'");

            service.Id = Guid.NewGuid();

            // Validate the service
            var validationResult = service.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"ServiceHandler: Validation failed: {string.Join(", ", validationResult.Errors)}");
                throw new InvalidOperationException($"Validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            var created = await _repository.AddAsync(service);
            
            _logger.LogInfo($"ServiceHandler: Service '{created.Name}' created successfully with ID {created.Id}");
            
            return created;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError("ServiceHandler: Database error creating service", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("ServiceHandler: Error creating service", ex);
            throw;
        }
    }

    public async Task<Service?> UpdateAsync(Guid id, Service service)
    {
        try
        {
            _logger.LogInfo($"ServiceHandler: Updating service {id}");

            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning($"ServiceHandler: Service {id} not found");
                return null;
            }

            existing.Name = service.Name;
            existing.Description = service.Description;

            // Validate the updated service
            var validationResult = existing.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"ServiceHandler: Validation failed: {string.Join(", ", validationResult.Errors)}");
                throw new InvalidOperationException($"Validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            var updated = await _repository.UpdateAsync(existing);
            
            _logger.LogInfo($"ServiceHandler: Service {id} updated successfully");
            
            return updated;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError($"ServiceHandler: Database error updating service {id}", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"ServiceHandler: Error updating service {id}", ex);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"ServiceHandler: Deleting service {id}");

            var service = await _repository.GetByIdAsync(id);
            if (service == null)
            {
                _logger.LogWarning($"ServiceHandler: Service {id} not found");
                return false;
            }

            await _repository.DeleteAsync(service);
            
            _logger.LogInfo($"ServiceHandler: Service {id} deleted successfully");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"ServiceHandler: Error deleting service {id}", ex);
            throw;
        }
    }
}
