using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

public interface IDeviceRepository
{
    Task<CustomerDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerDevice?> GetByInstallationIdAsync(Guid installationId, CancellationToken cancellationToken);
    Task<CustomerDevice?> GetByTokenHashAsync(byte[] tokenHash, CancellationToken cancellationToken);
    Task AddAsync(CustomerDevice device, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
