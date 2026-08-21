using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

public sealed class CheckoutDraftRepository : ICheckoutDraftRepository
{
    private readonly ApplicationDbContext _context;

    public CheckoutDraftRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task AddAsync(CheckoutDraft draft, CancellationToken cancellationToken) =>
        _context.CheckoutDrafts.AddAsync(draft, cancellationToken).AsTask();

    public Task<CheckoutDraft?> GetOwnedByOrderGuidAsync(
        Guid orderGuid,
        Guid ownerDeviceId,
        CancellationToken cancellationToken) =>
        _context.CheckoutDrafts
            .Include(draft => draft.Items)
                .ThenInclude(item => item.Selections)
            .SingleOrDefaultAsync(
                draft => draft.OrderGuid == orderGuid && draft.OwnerDeviceId == ownerDeviceId,
                cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _context.SaveChangesAsync(cancellationToken);
}
