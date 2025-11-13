using GhseeliApis.Models;

namespace GhseeliApis.Handlers.Interfaces;

public interface IServiceHandler
{
    Task<List<Service>> GetAllAsync();
    Task<Service?> GetByIdAsync(Guid id);
    Task<Service?> GetByIdWithOptionsAsync(Guid id);
    Task<Service> CreateAsync(Service service);
    Task<Service?> UpdateAsync(Guid id, Service service);
    Task<bool> DeleteAsync(Guid id);
}
