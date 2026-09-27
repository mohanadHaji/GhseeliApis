using FluentAssertions;
using Ghseeli.IntegrationContracts.Bookings;
using GhseeliApis.DTOs.Reviews;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Verifies SQL Server review constraints, rowversion concurrency, paging, and aggregates.
/// </summary>
public sealed class BusinessReviewRelationalIntegrationTests
{
    [Fact]
    public async Task Database_enforces_one_review_per_booking_and_rating_range()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var seeded = await SeedAsync(database);
        await using var context = database.CreateContext();
        context.BusinessReviews.Add(CreateReview(seeded.Booking, 5));
        await context.SaveChangesAsync();

        context.BusinessReviews.Add(CreateReview(seeded.Booking, 4));
        var duplicate = () => context.SaveChangesAsync();
        await duplicate.Should().ThrowAsync<DbUpdateException>();
        context.ChangeTracker.Clear();

        var invalidSeed = await SeedAsync(database, suffix: "invalid");
        var invalid = CreateReview(invalidSeed.Booking, 0);
        context.BusinessReviews.Add(invalid);
        var badRating = () => context.SaveChangesAsync();
        await badRating.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Stale_rowversion_update_and_delete_are_rejected()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var seeded = await SeedAsync(database, includeReview: true);
        var first = database.CreateContext();
        var second = database.CreateContext();
        await using (first)
        await using (second)
        {
            var firstReview = await first.BusinessReviews.SingleAsync();
            var staleReview = await second.BusinessReviews.SingleAsync();
            firstReview.Rating = 4;
            await first.SaveChangesAsync();

            staleReview.Rating = 3;
            var staleUpdate = () => second.SaveChangesAsync();
            await staleUpdate.Should().ThrowAsync<DbUpdateConcurrencyException>();
            second.ChangeTracker.Clear();

            var staleDelete = new BusinessReview
            {
                Id = firstReview.Id,
                CustomerBookingId = seeded.Booking.Id,
                UserId = seeded.User.Id,
                BusinessSourceId = seeded.Booking.BusinessSourceId,
                Rating = 4,
                CreatedAtUtc = firstReview.CreatedAtUtc,
                UpdatedAtUtc = firstReview.UpdatedAtUtc,
                RowVersion = staleReview.RowVersion
            };
            second.Attach(staleDelete);
            second.Remove(staleDelete);
            var delete = () => second.SaveChangesAsync();
            await delete.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }
    }

    [Fact]
    public async Task Concurrent_create_update_and_delete_each_commit_one_winner()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var seeded = await SeedAsync(database);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;

        await using (var first = new ApplicationDbContext(options))
        await using (var second = new ApplicationDbContext(options))
        {
            first.BusinessReviews.Add(CreateReview(seeded.Booking, 5));
            second.BusinessReviews.Add(CreateReview(seeded.Booking, 4));
            var createResults = await Task.WhenAll(
                CaptureSaveAsync(first),
                CaptureSaveAsync(second));
            createResults.Count(result => result is null).Should().Be(1);
            createResults.Count(result => result is DbUpdateException).Should().Be(1);
        }

        await using (var first = new ApplicationDbContext(options))
        await using (var second = new ApplicationDbContext(options))
        {
            var firstReview = await first.BusinessReviews.SingleAsync();
            var secondReview = await second.BusinessReviews.SingleAsync();
            firstReview.Rating = 3;
            secondReview.Rating = 2;
            var updateResults = await Task.WhenAll(
                CaptureSaveAsync(first),
                CaptureSaveAsync(second));
            updateResults.Count(result => result is null).Should().Be(1);
            updateResults.Count(result => result is DbUpdateConcurrencyException).Should().Be(1);
        }

        await using (var first = new ApplicationDbContext(options))
        await using (var second = new ApplicationDbContext(options))
        {
            first.BusinessReviews.Remove(await first.BusinessReviews.SingleAsync());
            second.BusinessReviews.Remove(await second.BusinessReviews.SingleAsync());
            var deleteResults = await Task.WhenAll(
                CaptureSaveAsync(first),
                CaptureSaveAsync(second));
            deleteResults.Count(result => result is null).Should().Be(1);
            deleteResults.Count(result => result is DbUpdateConcurrencyException).Should().Be(1);
        }

        await using var verify = new ApplicationDbContext(options);
        (await verify.BusinessReviews.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Public_repository_orders_deterministically_pages_and_rounds_batch_aggregate()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var seeded = await SeedAsync(database);
        await database.ExecuteAsync(context =>
        {
            var first = CreateReview(seeded.Booking, 4);
            first.Id = Guid.Parse("00000000-0000-0000-0000-000000000002");
            first.CreatedAtUtc = DateTimeOffset.Parse("2026-09-24T08:00:00Z");
            first.UpdatedAtUtc = first.CreatedAtUtc;
            context.BusinessReviews.Add(first);
        });

        var secondSeed = await SeedAsync(database, suffix: "second");
        await database.ExecuteAsync(context =>
        {
            var second = CreateReview(secondSeed.Booking, 5);
            second.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
            second.BusinessSourceId = seeded.Booking.BusinessSourceId;
            second.CreatedAtUtc = DateTimeOffset.Parse("2026-09-24T08:00:00Z");
            second.UpdatedAtUtc = second.CreatedAtUtc;
            context.BusinessReviews.Add(second);
        });

        await using var read = database.CreateContext();
        var repository = new BusinessReviewRepository(read);
        var page = await repository.GetPublicPageAsync(
            seeded.Booking.BusinessSourceId, 1, 1, default);
        var aggregate = await repository.GetAggregatesAsync(
            [seeded.Booking.BusinessSourceId], default);

        page.TotalCount.Should().Be(2);
        page.Items.Should().ContainSingle()
            .Which.Id.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        aggregate[seeded.Booking.BusinessSourceId].AverageRating.Should().Be(4.5m);
        aggregate[seeded.Booking.BusinessSourceId].RatingCount.Should().Be(2);
    }

    [Fact]
    public async Task Aggregate_repository_handles_empty_duplicate_zero_and_mixed_business_sets()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new BusinessReviewRepository(context);
        var empty = await repository.GetAggregatesAsync([], default);
        empty.Should().BeEmpty();

        var reviewed = await SeedAsync(database, includeReview: true);
        var zero = Guid.NewGuid();
        var values = await repository.GetAggregatesAsync(
            [reviewed.Booking.BusinessSourceId, zero, reviewed.Booking.BusinessSourceId],
            default);

        values.Should().HaveCount(2);
        values[reviewed.Booking.BusinessSourceId].Should().Be(
            new BusinessRatingAggregate(reviewed.Booking.BusinessSourceId, 5m, 1));
        values[zero].Should().Be(new BusinessRatingAggregate(zero, 0m, 0));
    }

    private static async Task<(User User, CustomerBooking Booking)> SeedAsync(
        SqlServerCatalogDatabase database,
        bool includeReview = false,
        string suffix = "first")
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"{suffix}@example.test",
            NormalizedUserName = $"{suffix}@example.test".ToUpperInvariant(),
            Email = $"{suffix}@example.test",
            NormalizedEmail = $"{suffix}@example.test".ToUpperInvariant(),
            FullName = $"{suffix} Customer",
            IsActive = true
        };
        var booking = CreateBooking(user.Id);
        await database.ExecuteAsync(context =>
        {
            context.Users.Add(user);
            context.CustomerBookings.Add(booking);
            if (includeReview)
            {
                context.BusinessReviews.Add(CreateReview(booking, 5));
            }
        });
        return (user, booking);
    }

    private static BusinessReview CreateReview(CustomerBooking booking, int rating) => new()
    {
        Id = Guid.NewGuid(),
        CustomerBookingId = booking.Id,
        UserId = booking.UserId,
        BusinessSourceId = booking.BusinessSourceId,
        Rating = rating,
        Comment = "review",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    private static CustomerBooking CreateBooking(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        PublicReference = Guid.NewGuid(),
        OrderGuid = Guid.NewGuid(),
        UserId = userId,
        OwnerDeviceId = Guid.NewGuid(),
        BusinessReservationId = Guid.NewGuid(),
        BusinessWorkOrderId = Guid.NewGuid(),
        BusinessSourceId = Guid.NewGuid(),
        BranchSourceId = Guid.NewGuid(),
        CatalogVersion = 1,
        ConfirmedDraftVersion = 1,
        Status = BookingStatuses.Completed,
        StatusChangedAtUtc = DateTimeOffset.UtcNow,
        RequestedSlotStartUtc = DateTimeOffset.UtcNow,
        RequestedSlotEndUtc = DateTimeOffset.UtcNow.AddHours(1),
        ProviderNameAr = "مزود",
        BranchNameAr = "فرع",
        VehicleType = "Sedan",
        AddressLine = "Street",
        Currency = "ILS",
        ServiceFeeMode = "None",
        TotalDurationMinutes = 60,
        QuotedAtUtc = DateTimeOffset.UtcNow,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static async Task<Exception?> CaptureSaveAsync(ApplicationDbContext context)
    {
        try
        {
            await context.SaveChangesAsync();
            return null;
        }
        catch (Exception exception) when (
            exception is DbUpdateException or DbUpdateConcurrencyException)
        {
            return exception;
        }
    }
}
