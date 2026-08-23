using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

public sealed class CheckoutDraftRepository : ICheckoutDraftRepository
{
    private readonly ApplicationDbContext _context;
    private bool _deferAcceptAllChanges;

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
            .Include(draft => draft.PricingSnapshot)
                .ThenInclude(snapshot => snapshot!.Items)
                    .ThenInclude(item => item.Selections)
            .SingleOrDefaultAsync(
                draft => draft.OrderGuid == orderGuid && draft.OwnerDeviceId == ownerDeviceId,
                cancellationToken);

    public void AddPricingSnapshot(CheckoutDraftPricingSnapshot snapshot) =>
        _context.CheckoutDraftPricingSnapshots.Add(snapshot);

    public void RemovePricingSnapshot(CheckoutDraftPricingSnapshot snapshot) =>
        _context.CheckoutDraftPricingSnapshots.Remove(snapshot);

    public Task<CheckoutDraftPersistenceState?> GetPersistenceStateForUpdateAsync(
        Guid draftId,
        CancellationToken cancellationToken)
    {
        var query = _context.Database.IsRelational()
            ? _context.CheckoutDrafts
                .FromSqlInterpolated(
                    $"SELECT * FROM [CheckoutDrafts] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {draftId}")
                .AsNoTracking()
            : _context.CheckoutDrafts.AsNoTracking().Where(draft => draft.Id == draftId);

        return query
            .Select(draft => new CheckoutDraftPersistenceState(
                draft.PublicVersion,
                draft.ExpiresAt,
                draft.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CheckoutDraftPricingPersistenceExpectation expectation,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return operation(cancellationToken);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return ExecuteWithDeferredAcceptanceAsync();

        async Task ExecuteWithDeferredAcceptanceAsync()
        {
            _deferAcceptAllChanges = true;
            try
            {
                await strategy.ExecuteAsync(
                    (Operation: operation, Expectation: expectation),
                    async (state, innerCancellationToken) =>
                    {
                        await using var transaction = await _context.Database.BeginTransactionAsync(
                            innerCancellationToken);
                        await state.Operation(innerCancellationToken);
                        await transaction.CommitAsync(innerCancellationToken);
                        return true;
                    },
                    async (state, innerCancellationToken) =>
                    {
                        var succeeded = await HasExpectedPricingPersistenceAsync(
                            state.Expectation,
                            innerCancellationToken);
                        return new Microsoft.EntityFrameworkCore.Storage.ExecutionResult<bool>(
                            succeeded,
                            succeeded);
                    },
                    cancellationToken);
                _context.ChangeTracker.AcceptAllChanges();
            }
            finally
            {
                _deferAcceptAllChanges = false;
            }
        }
    }

    private Task<bool> HasExpectedPricingPersistenceAsync(
        CheckoutDraftPricingPersistenceExpectation expectation,
        CancellationToken cancellationToken) =>
        _context.CheckoutDrafts
            .AsNoTracking()
            .AnyAsync(
                draft =>
                    draft.Id == expectation.DraftId &&
                    draft.PublicVersion == expectation.PublicVersion &&
                    draft.PricingSnapshot != null &&
                    draft.PricingSnapshot.Id == expectation.PricingSnapshotId,
                cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _context.SaveChangesAsync(
            acceptAllChangesOnSuccess: !_deferAcceptAllChanges,
            cancellationToken);
}
