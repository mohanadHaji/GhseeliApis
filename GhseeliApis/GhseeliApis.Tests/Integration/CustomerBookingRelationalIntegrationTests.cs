using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Verifies database-enforced customer booking idempotency.
/// </summary>
public class CustomerBookingRelationalIntegrationTests
{
    [Fact]
    public async Task ConfirmationClaim_BlocksConcurrentDraftMutationAndMakesItsWriteStale()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var draftId = Guid.NewGuid();
        await database.ExecuteAsync(context =>
        {
            context.CheckoutDrafts.Add(new CheckoutDraft
            {
                Id = draftId,
                OrderGuid = Guid.NewGuid(),
                OwnerDeviceId = Guid.NewGuid(),
                BusinessSourceId = Guid.NewGuid(),
                BranchSourceId = Guid.NewGuid(),
                CatalogVersion = 1,
                PublicVersion = 2,
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddHours(2),
                VehicleType = "Sedan",
                AddressLine = "Street 1",
                RequiresReprice = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
        });

        var options = new DbContextOptionsBuilder<GhseeliApis.Persistence.ApplicationDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;
        await using var confirmationContext = new GhseeliApis.Persistence.ApplicationDbContext(options);
        await using var mutationContext = new GhseeliApis.Persistence.ApplicationDbContext(options);
        var claimedDraft = await confirmationContext.CheckoutDrafts.SingleAsync(draft => draft.Id == draftId);
        var staleMutation = await mutationContext.CheckoutDrafts.SingleAsync(draft => draft.Id == draftId);
        await using var transaction = await confirmationContext.Database.BeginTransactionAsync();
        await confirmationContext.CheckoutDrafts
            .FromSqlInterpolated(
                $"SELECT * FROM [CheckoutDrafts] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {draftId}")
            .AsNoTracking()
            .SingleAsync();
        claimedDraft.ConfirmationClaimedVersion = claimedDraft.PublicVersion;
        claimedDraft.ConfirmationBookingReference = Guid.NewGuid();
        claimedDraft.ConfirmationClaimedAtUtc = DateTimeOffset.UtcNow;
        await confirmationContext.SaveChangesAsync();

        staleMutation.VehicleColor = "Red";
        staleMutation.PublicVersion++;
        var mutationSave = mutationContext.SaveChangesAsync();
        await Task.Delay(150);
        mutationSave.IsCompleted.Should().BeFalse();

        await transaction.CommitAsync();

        Func<Task> completeMutation = async () => await mutationSave;
        await completeMutation.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task CustomerBookings_WithDuplicateOrderGuid_SaveFailsAtDatabaseBoundary()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "booking@example.com",
            NormalizedUserName = "BOOKING@EXAMPLE.COM",
            Email = "booking@example.com",
            NormalizedEmail = "BOOKING@EXAMPLE.COM",
            FullName = "Booking Customer",
            IsActive = true
        };
        context.Users.Add(user);
        var orderGuid = Guid.NewGuid();
        context.CustomerBookings.Add(CreateBooking(user.Id, orderGuid));
        await context.SaveChangesAsync();

        context.CustomerBookings.Add(CreateBooking(user.Id, orderGuid));
        var action = () => context.SaveChangesAsync();

        await action.Should().ThrowAsync<DbUpdateException>();
    }

    private static CustomerBooking CreateBooking(Guid userId, Guid orderGuid) =>
        new()
        {
            Id = Guid.NewGuid(),
            PublicReference = Guid.NewGuid(),
            OrderGuid = orderGuid,
            UserId = userId,
            OwnerDeviceId = Guid.NewGuid(),
            BusinessReservationId = Guid.NewGuid(),
            BusinessWorkOrderId = Guid.NewGuid(),
            BusinessSourceId = Guid.NewGuid(),
            BranchSourceId = Guid.NewGuid(),
            CatalogVersion = 1,
            ConfirmedDraftVersion = 2,
            Status = "Reserved",
            RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddHours(1),
            RequestedSlotEndUtc = DateTimeOffset.UtcNow.AddHours(2),
            ProviderNameAr = "مزود",
            BranchNameAr = "فرع",
            VehicleType = "Sedan",
            AddressLine = "Street 1",
            Currency = "ILS",
            ServiceFeeMode = "None",
            TotalDurationMinutes = 60,
            QuotedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
}
