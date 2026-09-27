using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

public interface IBannerRepository
{
    Task<IReadOnlyList<Banner>> GetPublicAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Banner>> GetAdminAsync(CancellationToken cancellationToken);
    Task<Banner?> GetTrackedAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Banner banner, CancellationToken cancellationToken);
    void Delete(Banner banner);
    Task SaveAsync(CancellationToken cancellationToken);
}
