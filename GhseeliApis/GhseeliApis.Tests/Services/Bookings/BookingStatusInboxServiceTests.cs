using FluentAssertions;
using Ghseeli.IntegrationContracts.Bookings;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Business;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GhseeliApis.Tests.Services.Bookings;

/// <summary>
/// Verifies idempotent Customer inbox processing, ordering, references, and reconciliation.
/// </summary>
public sealed class BookingStatusInboxServiceTests
{
    [Theory]
    [MemberData(nameof(TransitionMatrix))]
    public async Task ApplyAsync_TransitionMatrix_EnforcesAllThirtySixEdges(
        string current,
        string target,
        bool expected)
    {
        var fixture = CreateFixture(current, 1);
        var message = fixture.Message(target, 2);

        var action = () => fixture.Service.ApplyAsync(
            message, Hash('8'), false, "corr-matrix", CancellationToken.None);

        if (expected)
        {
            var result = await action();
            result.Applied.Should().BeTrue();
            result.Status.Should().Be(target);
        }
        else
        {
            var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
            exception.Which.Code.Should().Be(BookingStatusErrorCodes.TransitionInvalid);
        }
    }

    public static IEnumerable<object[]> TransitionMatrix()
    {
        foreach (var current in BookingStatuses.All.Order())
        {
            foreach (var target in BookingStatuses.All.Order())
            {
                var expected =
                    current == BookingStatuses.Pending &&
                    target is BookingStatuses.Confirmed or BookingStatuses.Cancelled ||
                    current == BookingStatuses.Confirmed &&
                    target is BookingStatuses.InProgress or BookingStatuses.Cancelled or BookingStatuses.NoShow ||
                    current == BookingStatuses.InProgress &&
                    target is BookingStatuses.Completed or BookingStatuses.Cancelled or BookingStatuses.NoShow;
                yield return [current, target, expected];
            }
        }
    }

    [Fact]
    public async Task ApplyAsync_ValidEvent_UpdatesBookingAndInboxTogether()
    {
        var fixture = CreateFixture();
        var message = fixture.Message(BookingStatuses.Confirmed, 1);

        var result = await fixture.Service.ApplyAsync(
            message, Hash('a'), false, "corr", CancellationToken.None);

        result.Applied.Should().BeTrue();
        (await fixture.Context.CustomerBookings.SingleAsync()).Status
            .Should().Be(BookingStatuses.Confirmed);
        (await fixture.Context.ProcessedBookingStatusMessages.SingleAsync()).Applied
            .Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_DuplicateEventAndHash_ReplaysWithoutSecondMutation()
    {
        var fixture = CreateFixture();
        var message = fixture.Message(BookingStatuses.Confirmed, 1);
        await fixture.Service.ApplyAsync(message, Hash('a'), false, "corr", CancellationToken.None);

        var replay = await fixture.Service.ApplyAsync(
            message, Hash('a'), false, "corr", CancellationToken.None);

        replay.Status.Should().Be(BookingStatuses.Confirmed);
        (await fixture.Context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_DuplicateEventWithDifferentHash_RejectsConflict()
    {
        var fixture = CreateFixture();
        var message = fixture.Message(BookingStatuses.Confirmed, 1);
        await fixture.Service.ApplyAsync(message, Hash('a'), false, "corr", CancellationToken.None);

        var action = () => fixture.Service.ApplyAsync(
            message, Hash('b'), false, "corr", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.EventConflict);
    }

    [Fact]
    public async Task ApplyAsync_OlderSequence_IsRecordedButCannotMoveStateBackward()
    {
        var fixture = CreateFixture(BookingStatuses.InProgress, 2);
        var message = fixture.Message(BookingStatuses.Confirmed, 1);

        var result = await fixture.Service.ApplyAsync(
            message, Hash('c'), false, "corr", CancellationToken.None);

        result.Applied.Should().BeFalse();
        result.Stale.Should().BeTrue();
        result.Status.Should().Be(BookingStatuses.InProgress);
    }

    [Fact]
    public async Task ApplyAsync_AllowedEdgeWithSequenceGap_AppliesAuthoritativeSequence()
    {
        var fixture = CreateFixture();
        var message = fixture.Message(BookingStatuses.Confirmed, 4);

        var result = await fixture.Service.ApplyAsync(
            message, Hash('7'), false, "corr-gap", CancellationToken.None);

        result.Applied.Should().BeTrue();
        result.Sequence.Should().Be(4);
    }

    [Fact]
    public async Task ApplyAsync_NewEventWithEqualSequence_IsRejected()
    {
        var fixture = CreateFixture(BookingStatuses.Confirmed, 2);
        var message = fixture.Message(BookingStatuses.InProgress, 2);

        var action = () => fixture.Service.ApplyAsync(
            message, Hash('6'), false, "corr-equal", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.TransitionInvalid);
    }

    [Fact]
    public async Task ApplyAsync_RealCallbackAfterSyntheticReconciliation_IsRecordedAsNoOp()
    {
        var fixture = CreateFixture();
        fixture.BusinessClient.GetReservationStatusHandler = (_, _) =>
            Task.FromResult<AuthoritativeBookingStatusResponse?>(new()
            {
                BookingReference = fixture.Booking.PublicReference,
                ReservationId = fixture.Booking.BusinessReservationId,
                WorkOrderId = fixture.Booking.BusinessWorkOrderId,
                Status = BookingStatuses.Confirmed,
                Sequence = 1,
                ChangedAtUtc = DateTimeOffset.UtcNow
            });
        await fixture.Service.ReconcileAsync(
            fixture.Booking.PublicReference, "corr-reconcile", CancellationToken.None);
        var realEvent = fixture.Message(BookingStatuses.Confirmed, 1);

        var result = await fixture.Service.ApplyAsync(
            realEvent, Hash('4'), false, "corr-callback", CancellationToken.None);

        result.Applied.Should().BeFalse();
        result.Stale.Should().BeFalse();
        (await fixture.Context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(2);
        var recorded = await fixture.Context.ProcessedBookingStatusMessages
            .SingleAsync(value => value.EventId == realEvent.EventId);
        recorded.RequestHash.Should().Be(Hash('4'));
        recorded.Applied.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplyAsync_EqualSequenceAfterReconciliationWithConflict_IsRejected(
        bool conflictStatus)
    {
        var fixture = CreateFixture();
        fixture.BusinessClient.GetReservationStatusHandler = (_, _) =>
            Task.FromResult<AuthoritativeBookingStatusResponse?>(new()
            {
                BookingReference = fixture.Booking.PublicReference,
                ReservationId = fixture.Booking.BusinessReservationId,
                WorkOrderId = fixture.Booking.BusinessWorkOrderId,
                Status = BookingStatuses.Confirmed,
                Sequence = 1,
                ChangedAtUtc = DateTimeOffset.UtcNow
            });
        await fixture.Service.ReconcileAsync(
            fixture.Booking.PublicReference, "corr-reconcile", CancellationToken.None);
        var realEvent = fixture.Message(
            conflictStatus ? BookingStatuses.InProgress : BookingStatuses.Confirmed,
            1);
        if (!conflictStatus)
        {
            realEvent.WorkOrderId = Guid.NewGuid();
        }

        var action = () => fixture.Service.ApplyAsync(
            realEvent, Hash('3'), false, "corr-conflict", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
        exception.Which.Code.Should().Be(
            conflictStatus
                ? BookingStatusErrorCodes.TransitionInvalid
                : BookingStatusErrorCodes.ReferenceMismatch);
        (await fixture.Context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_SkippedImpossibleTransition_RejectsWithoutInboxOrMutation()
    {
        var fixture = CreateFixture();
        var message = fixture.Message(BookingStatuses.Completed, 3);

        var action = () => fixture.Service.ApplyAsync(
            message, Hash('d'), false, "corr", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.TransitionInvalid);
        (await fixture.Context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(0);
        (await fixture.Context.CustomerBookings.SingleAsync()).Status
            .Should().Be(BookingStatuses.Pending);
    }

    [Fact]
    public async Task ApplyAsync_ReferenceMismatch_RejectsWithoutChangingImmutableSnapshot()
    {
        var fixture = CreateFixture();
        var message = fixture.Message(BookingStatuses.Confirmed, 1);
        message.WorkOrderId = Guid.NewGuid();
        var total = (await fixture.Context.CustomerBookings.SingleAsync()).GrandTotal;

        var action = () => fixture.Service.ApplyAsync(
            message, Hash('e'), false, "corr", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.ReferenceMismatch);
        (await fixture.Context.CustomerBookings.SingleAsync()).GrandTotal.Should().Be(total);
    }

    [Fact]
    public async Task ApplyAsync_UnknownBooking_RejectsWithoutProcessedMessage()
    {
        var fixture = CreateFixture();
        var message = fixture.Message(BookingStatuses.Confirmed, 1);
        message.BookingReference = Guid.NewGuid();

        var action = () => fixture.Service.ApplyAsync(
            message, Hash('f'), false, "corr", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.NotFound);
        (await fixture.Context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_MalformedContract_RejectsBeforeDatabaseMutation()
    {
        var fixture = CreateFixture();
        var message = fixture.Message("confirmed", 0);
        message.ContractVersion = "v2";

        var action = () => fixture.Service.ApplyAsync(
            message, Hash('0'), false, "corr", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.Invalid);
        (await fixture.Context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(0);
        (await fixture.Context.CustomerBookings.SingleAsync()).Status
            .Should().Be(BookingStatuses.Pending);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public async Task ApplyAsync_NullOrMalformedRequestHash_RejectsStableContractError(
        string? requestHash)
    {
        var fixture = CreateFixture();

        var action = () => fixture.Service.ApplyAsync(
            fixture.Message(BookingStatuses.Confirmed, 1),
            requestHash!,
            false,
            "corr-hash",
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.Invalid);
    }

    [Fact]
    public async Task ApplyAsync_NullBody_RejectsStableContractError()
    {
        var fixture = CreateFixture();

        var action = () => fixture.Service.ApplyAsync(
            null!, Hash('5'), false, "corr-null", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.Invalid);
    }

    [Fact]
    public async Task ApplyAsync_WhenSaveReportsAmbiguousFailureAfterPersistence_ReplaysCommittedInbox()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AmbiguousSaveApplicationDbContext(options);
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "ambiguous@example.com",
            Email = "ambiguous@example.com",
            FullName = "Ambiguous Customer"
        };
        var booking = CreateFixtureBooking(user);
        context.CustomerBookings.Add(booking);
        await context.SaveChangesAsync();
        var service = new BookingStatusInboxService(
            context,
            new ScriptedBusinessApiClient(),
            new ManualTimeProvider(DateTimeOffset.UtcNow),
            new TestAppLogger());
        context.ThrowAfterNextSave = true;
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

        var result = await service.ApplyAsync(
            message, Hash('9'), false, "corr-ambiguous", CancellationToken.None);

        result.Status.Should().Be(BookingStatuses.Confirmed);
        (await context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ReconcileAsync_AllowedEdgeWithMissedSequence_RepairsToAuthoritativeState()
    {
        var fixture = CreateFixture();
        fixture.BusinessClient.GetReservationStatusHandler = (_, _) =>
            Task.FromResult<AuthoritativeBookingStatusResponse?>(new AuthoritativeBookingStatusResponse
            {
                BookingReference = fixture.Booking.PublicReference,
                ReservationId = fixture.Booking.BusinessReservationId,
                WorkOrderId = fixture.Booking.BusinessWorkOrderId,
                Status = BookingStatuses.Confirmed,
                Sequence = 3,
                ChangedAtUtc = DateTimeOffset.UtcNow
            });

        var result = await fixture.Service.ReconcileAsync(
            fixture.Booking.PublicReference, "corr-reconcile", CancellationToken.None);

        result.Applied.Should().BeTrue();
        result.Status.Should().Be(BookingStatuses.Confirmed);
        result.Sequence.Should().Be(3);
    }

    [Fact]
    public async Task ReconcileAsync_ForwardSequenceWithInvalidEdge_IsRejected()
    {
        var fixture = CreateFixture();
        fixture.BusinessClient.GetReservationStatusHandler = (_, _) =>
            Task.FromResult<AuthoritativeBookingStatusResponse?>(new()
            {
                BookingReference = fixture.Booking.PublicReference,
                ReservationId = fixture.Booking.BusinessReservationId,
                WorkOrderId = fixture.Booking.BusinessWorkOrderId,
                Status = BookingStatuses.Completed,
                Sequence = 3,
                ChangedAtUtc = DateTimeOffset.UtcNow
            });

        var action = () => fixture.Service.ReconcileAsync(
            fixture.Booking.PublicReference, "corr-reconcile-edge", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.TransitionInvalid);
        (await fixture.Context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReconcileAsync_AlreadyCurrent_IsNoOpWithoutSyntheticInboxEvent()
    {
        var fixture = CreateFixture(BookingStatuses.Confirmed, 2);
        fixture.BusinessClient.GetReservationStatusHandler = (_, _) =>
            Task.FromResult<AuthoritativeBookingStatusResponse?>(new()
            {
                BookingReference = fixture.Booking.PublicReference,
                ReservationId = fixture.Booking.BusinessReservationId,
                WorkOrderId = fixture.Booking.BusinessWorkOrderId,
                Status = BookingStatuses.Confirmed,
                Sequence = 2,
                ChangedAtUtc = DateTimeOffset.UtcNow
            });

        var result = await fixture.Service.ReconcileAsync(
            fixture.Booking.PublicReference, "corr-current", CancellationToken.None);

        result.Applied.Should().BeFalse();
        result.Stale.Should().BeFalse();
        (await fixture.Context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsOnlyStableReferencesAndOperationalState()
    {
        var fixture = CreateFixture(BookingStatuses.Confirmed, 2);

        var result = await fixture.Service.GetCurrentAsync(
            fixture.Booking.PublicReference,
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.BookingReference.Should().Be(fixture.Booking.PublicReference);
        result.ReservationId.Should().Be(fixture.Booking.BusinessReservationId);
        result.WorkOrderId.Should().Be(fixture.Booking.BusinessWorkOrderId);
        result.Status.Should().Be(BookingStatuses.Confirmed);
        result.Sequence.Should().Be(2);
    }

    [Theory]
    [InlineData("v2", BookingStatuses.Confirmed)]
    [InlineData(BookingStatusContract.Version, "Unknown")]
    public async Task ReconcileAsync_MalformedAuthoritativeContract_RejectsWithoutMutation(
        string version,
        string status)
    {
        var fixture = CreateFixture();
        fixture.BusinessClient.GetReservationStatusHandler = (_, _) =>
            Task.FromResult<AuthoritativeBookingStatusResponse?>(new()
            {
                ContractVersion = version,
                BookingReference = fixture.Booking.PublicReference,
                ReservationId = fixture.Booking.BusinessReservationId,
                WorkOrderId = fixture.Booking.BusinessWorkOrderId,
                Status = status,
                Sequence = 1,
                ChangedAtUtc = DateTimeOffset.UtcNow
            });

        var action = () => fixture.Service.ReconcileAsync(
            fixture.Booking.PublicReference, "corr-malformed", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusInboxException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.Invalid);
        (await fixture.Context.ProcessedBookingStatusMessages.CountAsync()).Should().Be(0);
        (await fixture.Context.CustomerBookings.SingleAsync()).Status
            .Should().Be(BookingStatuses.Pending);
    }

    private static Fixture CreateFixture(
        string status = BookingStatuses.Pending,
        long sequence = 0)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "status@example.com",
            Email = "status@example.com",
            FullName = "Status Customer"
        };
        var booking = CreateFixtureBooking(user, status, sequence);
        context.CustomerBookings.Add(booking);
        context.SaveChanges();
        var businessClient = new ScriptedBusinessApiClient();
        var service = new BookingStatusInboxService(
            context,
            businessClient,
            new ManualTimeProvider(DateTimeOffset.UtcNow),
            new TestAppLogger());
        return new Fixture(context, booking, businessClient, service);
    }

    private static CustomerBooking CreateFixtureBooking(
        User user,
        string status = BookingStatuses.Pending,
        long sequence = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            PublicReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            OwnerDeviceId = Guid.NewGuid(),
            BusinessReservationId = Guid.NewGuid(),
            BusinessWorkOrderId = Guid.NewGuid(),
            BusinessSourceId = Guid.NewGuid(),
            BranchSourceId = Guid.NewGuid(),
            CatalogVersion = 1,
            ConfirmedDraftVersion = 1,
            Status = status,
            BusinessStatusSequence = sequence,
            StatusChangedAtUtc = DateTimeOffset.UtcNow,
            ProviderNameAr = "مزود",
            BranchNameAr = "فرع",
            VehicleType = "Sedan",
            AddressLine = "Address",
            Currency = "ILS",
            ServiceFeeMode = "None",
            QuotedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

    private static string Hash(char value) => new(value, 64);

    private sealed record Fixture(
        ApplicationDbContext Context,
        CustomerBooking Booking,
        ScriptedBusinessApiClient BusinessClient,
        BookingStatusInboxService Service)
    {
        public BookingStatusChangedMessage Message(string status, long sequence) => new()
        {
            EventId = Guid.NewGuid(),
            BookingReference = Booking.PublicReference,
            ReservationId = Booking.BusinessReservationId,
            WorkOrderId = Booking.BusinessWorkOrderId,
            Status = status,
            Sequence = sequence,
            OccurredAtUtc = DateTimeOffset.UtcNow
        };
    }

    private sealed class AmbiguousSaveApplicationDbContext :
        ApplicationDbContext
    {
        public AmbiguousSaveApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public bool ThrowAfterNextSave { get; set; }

        public override async Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            if (ThrowAfterNextSave)
            {
                ThrowAfterNextSave = false;
                throw new DbUpdateException("Simulated ambiguous commit result.");
            }
            return result;
        }
    }
}
