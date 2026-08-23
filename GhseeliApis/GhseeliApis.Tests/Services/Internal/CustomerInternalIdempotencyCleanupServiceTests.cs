using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Internal;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Tests.Services.Internal;

/// <summary>
/// Verifies bounded, clock-driven cleanup without deleting active transport claims.
/// </summary>
public sealed class CustomerInternalIdempotencyCleanupServiceTests
{
    [Fact]
    public async Task CleanupExpiredAsync_DeletesOnlyBoundedExpiredCompletedOrAbandonedRecords()
    {
        var now = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);
        await using var database = await SqlDatabase.CreateAsync();
        await using var context = database.CreateContext();
        context.CustomerInternalIdempotencyRecords.AddRange(
            Record(CustomerInternalIdempotencyState.Completed, now.AddMinutes(-3)),
            Record(CustomerInternalIdempotencyState.InProgress, now.AddMinutes(-2)),
            Record(CustomerInternalIdempotencyState.Completed, now.AddMinutes(-1)),
            Record(CustomerInternalIdempotencyState.Completed, now.AddMinutes(1)),
            Record(CustomerInternalIdempotencyState.InProgress, now.AddMinutes(1)));
        await context.SaveChangesAsync();
        var service = new CustomerInternalIdempotencyCleanupService(
            context,
            new FixedTimeProvider(now),
            Options.Create(new CustomerInternalServiceOptions
            {
                ExpiredRecordCleanupBatchSize = 2
            }));

        var deleted = await service.CleanupExpiredAsync(CancellationToken.None);

        deleted.Should().Be(2);
        context.ChangeTracker.Clear();
        var remaining = await context.CustomerInternalIdempotencyRecords
            .AsNoTracking().ToListAsync();
        remaining.Should().HaveCount(3);
        remaining.Count(value =>
            value.State == CustomerInternalIdempotencyState.Completed &&
            value.ExpiresAtUtc <= now).Should().Be(1);
        remaining.Should().NotContain(value =>
            value.State == CustomerInternalIdempotencyState.InProgress &&
            value.ExpiresAtUtc <= now);
        remaining.Should().Contain(value =>
            value.State == CustomerInternalIdempotencyState.InProgress &&
            value.ExpiresAtUtc > now);
    }

    private static CustomerInternalIdempotencyRecord Record(
        CustomerInternalIdempotencyState state,
        DateTimeOffset expiresAt) => new()
        {
            ServiceId = "business-api",
            Operation = "booking-status-callback",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestHash = new string('a', 64),
            State = state,
            OwnerToken = state == CustomerInternalIdempotencyState.InProgress
                ? Guid.NewGuid()
                : null,
            LeaseExpiresAtUtc = state == CustomerInternalIdempotencyState.InProgress
                ? expiresAt
                : null,
            CreatedAtUtc = expiresAt.AddHours(-1),
            UpdatedAtUtc = expiresAt.AddMinutes(-1),
            ExpiresAtUtc = expiresAt
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SqlDatabase : IAsyncDisposable
    {
        private readonly string _name = $"GhseeliCustomerIdempotencyCleanup_{Guid.NewGuid():N}";

        public static async Task<SqlDatabase> CreateAsync()
        {
            var database = new SqlDatabase();
            await using var context = database.CreateContext();
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public ApplicationDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(ConnectionString)
                .Options);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var context = CreateContext();
                await context.Database.EnsureDeletedAsync();
            }
            catch (SqlException)
            {
            }
        }

        private string ConnectionString =>
            $"Server=(localdb)\\MSSQLLocalDB;Database={_name};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }
}
