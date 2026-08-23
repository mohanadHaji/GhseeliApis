using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

public interface ICheckoutDraftRepository
{
    Task AddAsync(CheckoutDraft draft, CancellationToken cancellationToken);

    Task<CheckoutDraft?> GetOwnedByOrderGuidAsync(
        Guid orderGuid,
        Guid ownerDeviceId,
        CancellationToken cancellationToken);

    void AddPricingSnapshot(CheckoutDraftPricingSnapshot snapshot);

    void RemovePricingSnapshot(CheckoutDraftPricingSnapshot snapshot);

    Task<CheckoutDraftPersistenceState?> GetPersistenceStateForUpdateAsync(
        Guid draftId,
        CancellationToken cancellationToken);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CheckoutDraftPricingPersistenceExpectation expectation,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record CheckoutDraftPersistenceState(
    int PublicVersion,
    DateTimeOffset ExpiresAt,
    byte[] RowVersion,
    bool RequiresReprice,
    Guid? PricingSnapshotId,
    int? ConfirmationClaimedVersion,
    Guid? ConfirmationBookingReference);

public sealed record CheckoutDraftPricingPersistenceExpectation(
    Guid DraftId,
    int PublicVersion,
    Guid PricingSnapshotId);
