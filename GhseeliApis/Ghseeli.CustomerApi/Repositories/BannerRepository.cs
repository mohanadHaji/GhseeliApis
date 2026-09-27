using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

public sealed class BannerRepository : IBannerRepository
{
    private readonly ApplicationDbContext _context;

    public BannerRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<Banner>> GetPublicAsync(
        CancellationToken cancellationToken) =>
        await _context.Banners
            .AsNoTracking()
            .Where(banner => banner.IsActive)
            .OrderBy(banner => banner.DisplayOrder)
            .ThenBy(banner => banner.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Banner>> GetAdminAsync(
        CancellationToken cancellationToken) =>
        await _context.Banners
            .AsNoTracking()
            .OrderBy(banner => banner.DisplayOrder)
            .ThenBy(banner => banner.Id)
            .ToListAsync(cancellationToken);

    public Task<Banner?> GetTrackedAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Banners.SingleOrDefaultAsync(banner => banner.Id == id, cancellationToken);

    public Task AddAsync(Banner banner, CancellationToken cancellationToken) =>
        _context.Banners.AddAsync(banner, cancellationToken).AsTask();

    public void Delete(Banner banner) => _context.Banners.Remove(banner);

    public Task SaveAsync(CancellationToken cancellationToken) =>
        _context.SaveChangesAsync(cancellationToken);
}
