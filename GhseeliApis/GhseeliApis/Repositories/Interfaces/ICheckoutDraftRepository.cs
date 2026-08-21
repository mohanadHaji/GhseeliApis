using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

public interface ICheckoutDraftRepository
{
    Task AddAsync(CheckoutDraft draft, CancellationToken cancellationToken);

    Task<CheckoutDraft?> GetOwnedByOrderGuidAsync(
        Guid orderGuid,
        Guid ownerDeviceId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
