using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Handlers;

/// <summary>
/// Handler for company-related business logic
/// </summary>
public class CompanyHandler : ICompanyHandler
{
    private readonly ICompanyRepository _repository;
    private readonly IAppLogger _logger;

    public CompanyHandler(ICompanyRepository repository, IAppLogger logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<Company>> GetAllAsync()
    {
        try
        {
            _logger.LogInfo("CompanyHandler: Getting all companies");
            return await _repository.GetAllAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("CompanyHandler: Error getting all companies", ex);
            throw;
        }
    }

    public async Task<Company?> GetByIdAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"CompanyHandler: Getting company {id}");
            return await _repository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError($"CompanyHandler: Error getting company {id}", ex);
            throw;
        }
    }

    public async Task<List<Company>> GetByServiceAreaAsync(string area)
    {
        try
        {
            _logger.LogInfo($"CompanyHandler: Getting companies in service area '{area}'");
            return await _repository.GetByServiceAreaAsync(area);
        }
        catch (Exception ex)
        {
            _logger.LogError($"CompanyHandler: Error getting companies in area '{area}'", ex);
            throw;
        }
    }

    public async Task<Company> CreateAsync(Company company)
    {
        try
        {
            _logger.LogInfo($"CompanyHandler: Creating company '{company.Name}'");

            company.Id = Guid.NewGuid();

            // Validate the company
            var validationResult = company.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"CompanyHandler: Validation failed: {string.Join(", ", validationResult.Errors)}");
                throw new InvalidOperationException($"Validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            var created = await _repository.AddAsync(company);
            
            _logger.LogInfo($"CompanyHandler: Company '{created.Name}' created successfully with ID {created.Id}");
            
            return created;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError("CompanyHandler: Database error creating company", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("CompanyHandler: Error creating company", ex);
            throw;
        }
    }

    public async Task<Company?> UpdateAsync(Guid id, Company company)
    {
        try
        {
            _logger.LogInfo($"CompanyHandler: Updating company {id}");

            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning($"CompanyHandler: Company {id} not found");
                return null;
            }

            existing.Name = company.Name;
            existing.Phone = company.Phone;
            existing.Description = company.Description;
            existing.ServiceAreaDescription = company.ServiceAreaDescription;

            // Validate the updated company
            var validationResult = existing.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"CompanyHandler: Validation failed: {string.Join(", ", validationResult.Errors)}");
                throw new InvalidOperationException($"Validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            var updated = await _repository.UpdateAsync(existing);
            
            _logger.LogInfo($"CompanyHandler: Company {id} updated successfully");
            
            return updated;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError($"CompanyHandler: Database error updating company {id}", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"CompanyHandler: Error updating company {id}", ex);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"CompanyHandler: Deleting company {id}");

            var company = await _repository.GetByIdAsync(id);
            if (company == null)
            {
                _logger.LogWarning($"CompanyHandler: Company {id} not found");
                return false;
            }

            await _repository.DeleteAsync(company);
            
            _logger.LogInfo($"CompanyHandler: Company {id} deleted successfully");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"CompanyHandler: Error deleting company {id}", ex);
            throw;
        }
    }
}
