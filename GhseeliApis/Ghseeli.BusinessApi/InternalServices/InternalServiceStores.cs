using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.InternalServices;

public interface IInternalServiceNonceStore
{
    Task<bool> TryAcceptAsync(
        string serviceId,
        string nonce,
        DateTime receivedAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken);
}

public interface IInternalIdempotencyStore
{
    Task<InternalIdempotencyClaimResult> ClaimAsync(
        string serviceId,
        string operation,
        string idempotencyKey,
        string requestHash,
        DateTime nowUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid recordId,
        int statusCode,
        string contentType,
        string responseBody,
        DateTime completedAtUtc,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        Guid recordId,
        CancellationToken cancellationToken);

    Task<InternalServiceIdempotencyRecord?> WaitForCompletionAsync(
        string serviceId,
        string operation,
        string idempotencyKey,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken);
}

public sealed class InternalIdempotencyClaimResult
{
    private InternalIdempotencyClaimResult(
        InternalIdempotencyClaimResultKind kind,
        Guid? recordId = null,
        InternalServiceIdempotencyRecord? record = null)
    {
        Kind = kind;
        RecordId = recordId;
        Record = record;
    }

    public InternalIdempotencyClaimResultKind Kind { get; }
    public Guid? RecordId { get; }
    public InternalServiceIdempotencyRecord? Record { get; }

    public static InternalIdempotencyClaimResult Acquired(Guid recordId) =>
        new(InternalIdempotencyClaimResultKind.Acquired, recordId);

    public static InternalIdempotencyClaimResult Replay(InternalServiceIdempotencyRecord record) =>
        new(InternalIdempotencyClaimResultKind.Replay, record: record);

    public static InternalIdempotencyClaimResult Conflict(InternalServiceIdempotencyRecord record) =>
        new(InternalIdempotencyClaimResultKind.Conflict, record: record);

    public static InternalIdempotencyClaimResult InProgress(InternalServiceIdempotencyRecord record) =>
        new(InternalIdempotencyClaimResultKind.InProgress, record: record);
}

public enum InternalIdempotencyClaimResultKind
{
    Acquired,
    Replay,
    Conflict,
    InProgress
}

public sealed class InternalServiceNonceStore : IInternalServiceNonceStore
{
    private readonly BusinessDbContext _dbContext;

    public InternalServiceNonceStore(BusinessDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> TryAcceptAsync(
        string serviceId,
        string nonce,
        DateTime receivedAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken)
    {
        await PruneExpiredAsync(receivedAtUtc, cancellationToken);

        var existing = await _dbContext.InternalServiceNonces
            .AnyAsync(
                record => record.ServiceId == serviceId &&
                    record.Nonce == nonce &&
                    record.ExpiresAtUtc > receivedAtUtc,
                cancellationToken);
        if (existing)
        {
            return false;
        }

        _dbContext.InternalServiceNonces.Add(new InternalServiceNonce
        {
            Id = Guid.NewGuid(),
            ServiceId = serviceId,
            Nonce = nonce,
            ReceivedAtUtc = receivedAtUtc,
            ExpiresAtUtc = expiresAtUtc
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task PruneExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var expired = await _dbContext.InternalServiceNonces
            .Where(record => record.ExpiresAtUtc <= nowUtc)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return;
        }

        _dbContext.InternalServiceNonces.RemoveRange(expired);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class InternalIdempotencyStore : IInternalIdempotencyStore
{
    private readonly BusinessDbContext _dbContext;

    public InternalIdempotencyStore(BusinessDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<InternalIdempotencyClaimResult> ClaimAsync(
        string serviceId,
        string operation,
        string idempotencyKey,
        string requestHash,
        DateTime nowUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken)
    {
        await PruneExpiredAsync(nowUtc, cancellationToken);

        while (true)
        {
            var existing = await _dbContext.InternalServiceIdempotencyRecords
                .SingleOrDefaultAsync(
                    record => record.ServiceId == serviceId &&
                        record.Operation == operation &&
                        record.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (existing is null)
            {
                var record = new InternalServiceIdempotencyRecord
                {
                    Id = Guid.NewGuid(),
                    ServiceId = serviceId,
                    Operation = operation,
                    IdempotencyKey = idempotencyKey,
                    RequestHash = requestHash,
                    State = InternalServiceIdempotencyState.InProgress,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                    ExpiresAtUtc = expiresAtUtc
                };
                _dbContext.InternalServiceIdempotencyRecords.Add(record);

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return InternalIdempotencyClaimResult.Acquired(record.Id);
                }
                catch (DbUpdateException)
                {
                    _dbContext.Entry(record).State = EntityState.Detached;

                    existing = await _dbContext.InternalServiceIdempotencyRecords
                        .SingleOrDefaultAsync(
                            persisted => persisted.ServiceId == serviceId &&
                                persisted.Operation == operation &&
                                persisted.IdempotencyKey == idempotencyKey,
                            cancellationToken);
                    if (existing is null)
                    {
                        continue;
                    }
                }
            }

            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return InternalIdempotencyClaimResult.Conflict(existing);
            }

            return existing.State == InternalServiceIdempotencyState.Completed
                ? InternalIdempotencyClaimResult.Replay(existing)
                : InternalIdempotencyClaimResult.InProgress(existing);
        }
    }

    public async Task CompleteAsync(
        Guid recordId,
        int statusCode,
        string contentType,
        string responseBody,
        DateTime completedAtUtc,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.InternalServiceIdempotencyRecords
            .SingleOrDefaultAsync(item => item.Id == recordId, cancellationToken);

        if (record is null)
        {
            return;
        }

        record.State = InternalServiceIdempotencyState.Completed;
        record.StatusCode = statusCode;
        record.ContentType = contentType;
        record.ResponseBody = responseBody;
        record.UpdatedAtUtc = completedAtUtc;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var trackedRecord = _dbContext.ChangeTracker
            .Entries<InternalServiceIdempotencyRecord>()
            .SingleOrDefault(entry => entry.Entity.Id == recordId)?
            .Entity;
        var record = trackedRecord ?? await _dbContext.InternalServiceIdempotencyRecords
            .SingleOrDefaultAsync(item => item.Id == recordId, cancellationToken);

        if (record is null)
        {
            return;
        }

        _dbContext.InternalServiceIdempotencyRecords.Remove(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<InternalServiceIdempotencyRecord?> WaitForCompletionAsync(
        string serviceId,
        string operation,
        string idempotencyKey,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        var timeoutAt = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow <= timeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var record = await _dbContext.InternalServiceIdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.ServiceId == serviceId &&
                        item.Operation == operation &&
                        item.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (record is null)
            {
                return null;
            }

            if (record?.State == InternalServiceIdempotencyState.Completed)
            {
                return record;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return null;
    }

    private async Task PruneExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var expired = await _dbContext.InternalServiceIdempotencyRecords
            .Where(record => record.ExpiresAtUtc <= nowUtc)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return;
        }

        _dbContext.InternalServiceIdempotencyRecords.RemoveRange(expired);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
