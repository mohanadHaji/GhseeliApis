using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;

namespace GhseeliApis.Services.Internal;

public enum CustomerInternalIdempotencyClaimKind
{
    Acquired,
    Replay,
    Conflict,
    InProgress
}

public sealed record CustomerInternalIdempotencyClaim(
    CustomerInternalIdempotencyClaimKind Kind,
    Guid RecordId,
    Guid OwnerToken,
    CustomerInternalIdempotencyRecord? Record);

public interface ICustomerInternalIdempotencyLeaseService
{
    Task<CustomerInternalIdempotencyClaim> ClaimAsync(
        string serviceId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken);

    Task<DateTimeOffset?> RenewAsync(
        Guid recordId,
        Guid ownerToken,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        Guid recordId,
        Guid ownerToken,
        int statusCode,
        string contentType,
        byte[] responseBody,
        CancellationToken cancellationToken);

    Task<bool> ReleaseAsync(
        Guid recordId,
        Guid ownerToken,
        CancellationToken cancellationToken);
}

public sealed class CustomerInternalIdempotencyLeaseService :
    ICustomerInternalIdempotencyLeaseService
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly CustomerInternalServiceOptions _options;

    public CustomerInternalIdempotencyLeaseService(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        IOptions<CustomerInternalServiceOptions> options)
    {
        _context = context;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public async Task<CustomerInternalIdempotencyClaim> ClaimAsync(
        string serviceId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var strategy = _context.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    var now = _timeProvider.GetUtcNow();
                    _context.ChangeTracker.Clear();
                    await using var transaction = _context.Database.IsRelational()
                        ? await _context.Database.BeginTransactionAsync(
                            IsolationLevel.Serializable, cancellationToken)
                        : null;
                    var query = _context.Database.IsRelational()
                        ? _context.CustomerInternalIdempotencyRecords.FromSqlInterpolated(
                            $"SELECT * FROM [CustomerInternalIdempotencyRecords] WITH (UPDLOCK, HOLDLOCK) WHERE [ServiceId] = {serviceId} AND [Operation] = {operation} AND [IdempotencyKey] = {key}")
                        : _context.CustomerInternalIdempotencyRecords.Where(value =>
                            value.ServiceId == serviceId &&
                            value.Operation == operation &&
                            value.IdempotencyKey == key);
                    var record = await query.SingleOrDefaultAsync(cancellationToken);

                    if (record is not null && record.ExpiresAtUtc > now &&
                        !string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
                    {
                        if (transaction is not null)
                            await transaction.CommitAsync(cancellationToken);
                        return Result(CustomerInternalIdempotencyClaimKind.Conflict, record);
                    }
                    if (record is not null &&
                        record.State == CustomerInternalIdempotencyState.Completed &&
                        record.ExpiresAtUtc > now)
                    {
                        if (transaction is not null)
                            await transaction.CommitAsync(cancellationToken);
                        return Result(CustomerInternalIdempotencyClaimKind.Replay, record);
                    }
                    if (record is not null &&
                        record.State == CustomerInternalIdempotencyState.InProgress &&
                        record.LeaseExpiresAtUtc > now)
                    {
                        if (transaction is not null)
                            await transaction.CommitAsync(cancellationToken);
                        return Result(CustomerInternalIdempotencyClaimKind.InProgress, record);
                    }

                    if (record is null)
                    {
                        record = new CustomerInternalIdempotencyRecord
                        {
                            ServiceId = serviceId,
                            Operation = operation,
                            IdempotencyKey = key,
                            CreatedAtUtc = now
                        };
                        _context.CustomerInternalIdempotencyRecords.Add(record);
                    }

                    var ownerToken = Guid.NewGuid();
                    record.RequestHash = requestHash;
                    record.State = CustomerInternalIdempotencyState.InProgress;
                    record.OwnerToken = ownerToken;
                    record.LeaseExpiresAtUtc =
                        now.AddSeconds(_options.InProgressRecoverySeconds);
                    record.ResponseStatusCode = null;
                    record.ResponseContentType = null;
                    record.ResponseBody = null;
                    record.UpdatedAtUtc = now;
                    record.ExpiresAtUtc = now.AddSeconds(_options.IdempotencyLifetimeSeconds);
                    await _context.SaveChangesAsync(cancellationToken);
                    if (transaction is not null)
                        await transaction.CommitAsync(cancellationToken);
                    return new CustomerInternalIdempotencyClaim(
                        CustomerInternalIdempotencyClaimKind.Acquired,
                        record.Id,
                        ownerToken,
                        record);
                });
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                _context.ChangeTracker.Clear();
            }
        }

        return new CustomerInternalIdempotencyClaim(
            CustomerInternalIdempotencyClaimKind.InProgress,
            Guid.Empty,
            Guid.Empty,
            null);
    }

    public async Task<DateTimeOffset?> RenewAsync(
        Guid recordId,
        Guid ownerToken,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var expiry = now.AddSeconds(_options.InProgressRecoverySeconds);
        var renewed = await UpdateOwnedAsync(
            recordId,
            ownerToken,
            record =>
            {
                record.LeaseExpiresAtUtc = expiry;
                record.UpdatedAtUtc = now;
            },
            query => query.ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    record => record.LeaseExpiresAtUtc,
                    expiry)
                .SetProperty(record => record.UpdatedAtUtc, now),
                cancellationToken),
            cancellationToken,
            now);
        return renewed ? expiry : null;
    }

    public Task<bool> CompleteAsync(
        Guid recordId,
        Guid ownerToken,
        int statusCode,
        string contentType,
        byte[] responseBody,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        return UpdateOwnedAsync(
            recordId,
            ownerToken,
            record =>
            {
                record.State = CustomerInternalIdempotencyState.Completed;
                record.OwnerToken = null;
                record.LeaseExpiresAtUtc = null;
                record.ResponseStatusCode = statusCode;
                record.ResponseContentType = contentType;
                record.ResponseBody = responseBody;
                record.UpdatedAtUtc = now;
                record.ExpiresAtUtc = now.AddSeconds(_options.IdempotencyLifetimeSeconds);
            },
            query => query.ExecuteUpdateAsync(setters => setters
                .SetProperty(record => record.State, CustomerInternalIdempotencyState.Completed)
                .SetProperty(record => record.OwnerToken, (Guid?)null)
                .SetProperty(record => record.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(record => record.ResponseStatusCode, statusCode)
                .SetProperty(record => record.ResponseContentType, contentType)
                .SetProperty(record => record.ResponseBody, responseBody)
                .SetProperty(record => record.UpdatedAtUtc, now)
                .SetProperty(
                    record => record.ExpiresAtUtc,
                    now.AddSeconds(_options.IdempotencyLifetimeSeconds)),
                cancellationToken),
            cancellationToken,
            now);
    }

    public async Task<bool> ReleaseAsync(
        Guid recordId,
        Guid ownerToken,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        var query = OwnedQuery(recordId, ownerToken);
        if (_context.Database.IsRelational())
        {
            return await query.ExecuteDeleteAsync(cancellationToken) == 1;
        }

        var record = await query.SingleOrDefaultAsync(cancellationToken);
        if (record is null)
            return false;
        _context.CustomerInternalIdempotencyRecords.Remove(record);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> UpdateOwnedAsync(
        Guid recordId,
        Guid ownerToken,
        Action<CustomerInternalIdempotencyRecord> update,
        Func<IQueryable<CustomerInternalIdempotencyRecord>, Task<int>> relationalUpdate,
        CancellationToken cancellationToken,
        DateTimeOffset? leaseMustBeActiveAt = null)
    {
        _context.ChangeTracker.Clear();
        var query = OwnedQuery(recordId, ownerToken, leaseMustBeActiveAt);
        if (_context.Database.IsRelational())
        {
            return await relationalUpdate(query) == 1;
        }

        var record = await query.SingleOrDefaultAsync(cancellationToken);
        if (record is null)
            return false;
        update(record);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<CustomerInternalIdempotencyRecord> OwnedQuery(
        Guid recordId,
        Guid ownerToken,
        DateTimeOffset? leaseMustBeActiveAt = null)
    {
        var query = _context.CustomerInternalIdempotencyRecords.Where(record =>
            record.Id == recordId &&
            record.State == CustomerInternalIdempotencyState.InProgress &&
            record.OwnerToken == ownerToken);
        return leaseMustBeActiveAt is null
            ? query
            : query.Where(record =>
                record.LeaseExpiresAtUtc > leaseMustBeActiveAt.Value);
    }

    private static CustomerInternalIdempotencyClaim Result(
        CustomerInternalIdempotencyClaimKind kind,
        CustomerInternalIdempotencyRecord record) =>
        new(kind, record.Id, record.OwnerToken ?? Guid.Empty, record);
}
