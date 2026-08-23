using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Services.Internal;

public interface ICustomerInternalIdempotencyCleanupService
{
    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken);
}

public sealed class CustomerInternalIdempotencyCleanupService :
    ICustomerInternalIdempotencyCleanupService
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly CustomerInternalServiceOptions _options;

    public CustomerInternalIdempotencyCleanupService(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        IOptions<CustomerInternalServiceOptions> options)
    {
        _context = context;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var batchSize = Math.Clamp(_options.ExpiredRecordCleanupBatchSize, 1, 1_000);
        var ids = await _context.CustomerInternalIdempotencyRecords
            .AsNoTracking()
            .Where(record =>
                (record.State == CustomerInternalIdempotencyState.Completed &&
                 record.ExpiresAtUtc <= now) ||
                (record.State == CustomerInternalIdempotencyState.InProgress &&
                 (record.LeaseExpiresAtUtc == null ||
                  record.LeaseExpiresAtUtc <= now)))
            .OrderBy(record => record.State == CustomerInternalIdempotencyState.Completed
                ? record.ExpiresAtUtc
                : record.LeaseExpiresAtUtc)
            .Select(record => record.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
        if (ids.Length == 0)
        {
            return 0;
        }

        if (_context.Database.IsRelational())
        {
            return await _context.CustomerInternalIdempotencyRecords
                .Where(record =>
                    ids.Contains(record.Id) &&
                    ((record.State == CustomerInternalIdempotencyState.Completed &&
                      record.ExpiresAtUtc <= now) ||
                     (record.State == CustomerInternalIdempotencyState.InProgress &&
                      (record.LeaseExpiresAtUtc == null ||
                       record.LeaseExpiresAtUtc <= now))))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var records = await _context.CustomerInternalIdempotencyRecords
            .Where(record =>
                ids.Contains(record.Id) &&
                ((record.State == CustomerInternalIdempotencyState.Completed &&
                  record.ExpiresAtUtc <= now) ||
                 (record.State == CustomerInternalIdempotencyState.InProgress &&
                  (record.LeaseExpiresAtUtc == null ||
                   record.LeaseExpiresAtUtc <= now))))
            .ToArrayAsync(cancellationToken);
        _context.CustomerInternalIdempotencyRecords.RemoveRange(records);
        await _context.SaveChangesAsync(cancellationToken);
        return records.Length;
    }
}
