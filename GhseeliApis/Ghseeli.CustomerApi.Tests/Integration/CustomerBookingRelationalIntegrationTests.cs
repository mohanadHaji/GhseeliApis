using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Tests.Support;
using GhseeliApis.Services.Bookings;
using Ghseeli.IntegrationContracts.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Verifies database-enforced customer booking idempotency.
/// </summary>
public class CustomerBookingRelationalIntegrationTests
{
    [Fact]
    public async Task StatusInbox_PostCommitAcknowledgementFailure_VerifiesStableEventWithoutDuplicate()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var booking = await SeedStatusBookingAsync(database);
        var interceptor = new FailFirstCommitAcknowledgementInterceptor();
        var options = new DbContextOptionsBuilder<GhseeliApis.Persistence.ApplicationDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(interceptor)
            .ReplaceService<IExecutionStrategyFactory, TestRetryingExecutionStrategyFactory>()
            .Options;
        await using var context = new GhseeliApis.Persistence.ApplicationDbContext(options);
        var service = CreateStatusService(context);
        var eventId = Guid.NewGuid();
        var requestHash = new string('f', 64);
        var message = new BookingStatusChangedMessage
        {
            EventId = eventId,
            BookingReference = booking.PublicReference,
            ReservationId = booking.BusinessReservationId,
            WorkOrderId = booking.BusinessWorkOrderId,
            Status = BookingStatuses.Confirmed,
            Sequence = 1,
            OccurredAtUtc = DateTimeOffset.UtcNow
        };

        var result = await service.ApplyAsync(
            message,
            requestHash,
            false,
            "corr-post-commit",
            CancellationToken.None);

        result.EventId.Should().Be(eventId);
        result.Applied.Should().BeTrue();
        interceptor.CommitAttempts.Should().Be(1);
        await using var verify = database.CreateContext();
        var persisted = await verify.ProcessedBookingStatusMessages.SingleAsync();
        persisted.EventId.Should().Be(eventId);
        persisted.RequestHash.Should().Be(requestHash);
        (await verify.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
        var persistedBooking = await verify.CustomerBookings.SingleAsync();
        persistedBooking.Status.Should().Be(BookingStatuses.Confirmed);
        persistedBooking.BusinessStatusSequence.Should().Be(1);
    }

    [Fact]
    public async Task StatusInbox_ConcurrentIdenticalEvent_CommitsOneMessageAndOneTransition()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var booking = await SeedStatusBookingAsync(database);
        var options = new DbContextOptionsBuilder<GhseeliApis.Persistence.ApplicationDbContext>()
            .UseSqlServer(
                database.ConnectionString,
                sql => sql.EnableRetryOnFailure(5))
            .Options;
        await using var firstContext = new GhseeliApis.Persistence.ApplicationDbContext(options);
        await using var secondContext = new GhseeliApis.Persistence.ApplicationDbContext(options);
        var firstService = CreateStatusService(firstContext);
        var secondService = CreateStatusService(secondContext);
        var message = new BookingStatusChangedMessage
        {
            EventId = Guid.NewGuid(),
            BookingReference = booking.PublicReference,
            ReservationId = booking.BusinessReservationId,
            WorkOrderId = booking.BusinessWorkOrderId,
            Status = BookingStatuses.Confirmed,
            Sequence = 1,
            OccurredAtUtc = DateTimeOffset.UtcNow
        };

        var results = await Task.WhenAll(
            firstService.ApplyAsync(message, new string('a', 64), false, "corr-a", CancellationToken.None),
            secondService.ApplyAsync(message, new string('a', 64), false, "corr-b", CancellationToken.None));

        results.Should().OnlyContain(result => result.Status == BookingStatuses.Confirmed);
        await using var verify = database.CreateContext();
        (await verify.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
        var persisted = await verify.CustomerBookings.SingleAsync();
        persisted.Status.Should().Be(BookingStatuses.Confirmed);
        persisted.BusinessStatusSequence.Should().Be(1);
    }

    [Fact]
    public async Task StatusInbox_ConcurrentDifferentEventsAtSameSequence_CommitsOneWinner()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var booking = await SeedStatusBookingAsync(database);
        var options = new DbContextOptionsBuilder<GhseeliApis.Persistence.ApplicationDbContext>()
            .UseSqlServer(database.ConnectionString, sql => sql.EnableRetryOnFailure(5))
            .Options;
        await using var firstContext = new GhseeliApis.Persistence.ApplicationDbContext(options);
        await using var secondContext = new GhseeliApis.Persistence.ApplicationDbContext(options);
        var first = CreateStatusService(firstContext);
        var second = CreateStatusService(secondContext);
        BookingStatusChangedMessage Message() => new()
        {
            EventId = Guid.NewGuid(),
            BookingReference = booking.PublicReference,
            ReservationId = booking.BusinessReservationId,
            WorkOrderId = booking.BusinessWorkOrderId,
            Status = BookingStatuses.Confirmed,
            Sequence = 1,
            OccurredAtUtc = DateTimeOffset.UtcNow
        };

        var results = await Task.WhenAll(
            CaptureAsync(() => first.ApplyAsync(
                Message(), new string('b', 64), false, "corr-first", CancellationToken.None)),
            CaptureAsync(() => second.ApplyAsync(
                Message(), new string('c', 64), false, "corr-second", CancellationToken.None)));

        results.Count(result => result.Response is not null).Should().Be(1);
        results.Count(result =>
            result.Error?.Code == BookingStatusErrorCodes.TransitionInvalid).Should().Be(1);
        await using var verify = database.CreateContext();
        (await verify.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
        var persisted = await verify.CustomerBookings.SingleAsync();
        persisted.Status.Should().Be(BookingStatuses.Confirmed);
        persisted.BusinessStatusSequence.Should().Be(1);
    }

    [Fact]
    public async Task StatusInbox_SimultaneousReconciliationAndRealCallback_RecordsRealIdentityOnce()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var booking = await SeedStatusBookingAsync(database);
        var options = new DbContextOptionsBuilder<GhseeliApis.Persistence.ApplicationDbContext>()
            .UseSqlServer(database.ConnectionString, sql => sql.EnableRetryOnFailure(5))
            .Options;
        await using var reconcileContext = new GhseeliApis.Persistence.ApplicationDbContext(options);
        await using var callbackContext = new GhseeliApis.Persistence.ApplicationDbContext(options);
        var authoritative = new AuthoritativeBookingStatusResponse
        {
            BookingReference = booking.PublicReference,
            ReservationId = booking.BusinessReservationId,
            WorkOrderId = booking.BusinessWorkOrderId,
            Status = BookingStatuses.Confirmed,
            Sequence = 1,
            ChangedAtUtc = DateTimeOffset.UtcNow
        };
        var businessClient = new ScriptedBusinessApiClient
        {
            GetReservationStatusHandler = (_, _) =>
                Task.FromResult<AuthoritativeBookingStatusResponse?>(authoritative)
        };
        var reconcileService = new BookingStatusInboxService(
            reconcileContext,
            businessClient,
            new ManualTimeProvider(DateTimeOffset.UtcNow),
            new TestAppLogger());
        var callbackService = CreateStatusService(callbackContext);
        var realEvent = new BookingStatusChangedMessage
        {
            EventId = Guid.NewGuid(),
            BookingReference = booking.PublicReference,
            ReservationId = booking.BusinessReservationId,
            WorkOrderId = booking.BusinessWorkOrderId,
            Status = BookingStatuses.Confirmed,
            Sequence = 1,
            OccurredAtUtc = authoritative.ChangedAtUtc
        };

        var results = await Task.WhenAll(
            reconcileService.ReconcileAsync(
                booking.PublicReference, "corr-reconcile", CancellationToken.None),
            callbackService.ApplyAsync(
                realEvent, new string('d', 64), false, "corr-callback", CancellationToken.None));

        results.Should().OnlyContain(result => result.Status == BookingStatuses.Confirmed);
        await using var verify = database.CreateContext();
        var real = await verify.ProcessedBookingStatusMessages
            .SingleAsync(value => value.EventId == realEvent.EventId);
        real.RequestHash.Should().Be(new string('d', 64));
        (await verify.CustomerBookings.SingleAsync()).BusinessStatusSequence.Should().Be(1);
    }

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

    private static BookingStatusInboxService CreateStatusService(
        GhseeliApis.Persistence.ApplicationDbContext context) =>
        new(
            context,
            new ScriptedBusinessApiClient(),
            new ManualTimeProvider(DateTimeOffset.UtcNow),
            new TestAppLogger());

    private sealed class TestTransientException : Exception;

    private sealed class FailFirstCommitAcknowledgementInterceptor : DbTransactionInterceptor
    {
        private int _commitAttempts;
        public int CommitAttempts => _commitAttempts;

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _commitAttempts) == 1)
            {
                throw new TestTransientException();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class TestRetryingExecutionStrategyFactory : IExecutionStrategyFactory
    {
        private readonly ExecutionStrategyDependencies _dependencies;
        public TestRetryingExecutionStrategyFactory(ExecutionStrategyDependencies dependencies) =>
            _dependencies = dependencies;
        public IExecutionStrategy Create() => new TestRetryingExecutionStrategy(_dependencies);
    }

    private sealed class TestRetryingExecutionStrategy : ExecutionStrategy
    {
        public TestRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
            : base(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
        {
        }

        protected override bool ShouldRetryOn(Exception exception) =>
            exception is TestTransientException;
    }

    private static async Task<(BookingStatusCallbackResponse? Response, BookingStatusInboxException? Error)>
        CaptureAsync(Func<Task<BookingStatusCallbackResponse>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (BookingStatusInboxException exception)
        {
            return (null, exception);
        }
    }

    private static async Task<CustomerBooking> SeedStatusBookingAsync(
        SqlServerCatalogDatabase database)
    {
        CustomerBooking? booking = null;
        await database.ExecuteAsync(context =>
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = "status-relational@example.com",
                NormalizedUserName = "STATUS-RELATIONAL@EXAMPLE.COM",
                Email = "status-relational@example.com",
                NormalizedEmail = "STATUS-RELATIONAL@EXAMPLE.COM",
                FullName = "Status Relational",
                IsActive = true
            };
            booking = CreateBooking(user.Id, Guid.NewGuid());
            booking.User = user;
            booking.Status = BookingStatuses.Pending;
            booking.BusinessStatusSequence = 0;
            booking.StatusChangedAtUtc = DateTimeOffset.UtcNow;
            context.CustomerBookings.Add(booking);
        });
        return booking!;
    }
}
