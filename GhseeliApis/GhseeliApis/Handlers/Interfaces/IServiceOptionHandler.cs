using GhseeliApis.Models;

namespace GhseeliApis.Handlers.Interfaces;

/// <summary>
/// Interface for service option-related business logic
/// </summary>
public interface IServiceOptionHandler
{
    Task<List<ServiceOption>> GetAllAsync();
    Task<List<ServiceOption>> GetByServiceIdAsync(Guid serviceId);
    Task<List<ServiceOption>> GetByCompanyIdAsync(Guid companyId);
    Task<ServiceOption?> GetByIdAsync(Guid id);
    Task<ServiceOption> CreateAsync(ServiceOption serviceOption);
    Task<ServiceOption?> UpdateAsync(Guid id, ServiceOption serviceOption);
    Task<bool> DeleteAsync(Guid id);
}
