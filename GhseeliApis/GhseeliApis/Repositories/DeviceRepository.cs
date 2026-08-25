using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

public sealed class DeviceRepository : IDeviceRepository
{
    private readonly ApplicationDbContext _context;

    public DeviceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<CustomerDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _context.CustomerDevices.SingleOrDefaultAsync(device => device.Id == id, cancellationToken);

    public Task<CustomerDevice?> GetByInstallationIdAsync(
        Guid installationId,
        CancellationToken cancellationToken) =>
        _context.CustomerDevices.SingleOrDefaultAsync(
            device => device.InstallationId == installationId,
            cancellationToken);

    public Task<CustomerDevice?> GetByTokenHashAsync(
        byte[] tokenHash,
        CancellationToken cancellationToken) =>
        _context.CustomerDevices.SingleOrDefaultAsync(
            device => device.TokenHash.SequenceEqual(tokenHash),
            cancellationToken);

    public Task AddAsync(CustomerDevice device, CancellationToken cancellationToken) =>
        _context.CustomerDevices.AddAsync(device, cancellationToken).AsTask();

    public void Detach(CustomerDevice device) =>
        _context.Entry(device).State = EntityState.Detached;

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _context.SaveChangesAsync(cancellationToken);
}
