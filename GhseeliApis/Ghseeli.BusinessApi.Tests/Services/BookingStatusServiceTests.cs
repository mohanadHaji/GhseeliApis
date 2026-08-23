using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.Bookings;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies authoritative work-order transitions and transactional outbox creation.
/// </summary>
public sealed class BookingStatusServiceTests
{
    [Theory]
    [MemberData(nameof(TransitionMatrix))]
    public void TransitionMatrix_OnlyAllowsForwardDomainTransitions(
        string current,
        string target,
        bool expected)
    {
        BookingStatusTransitionRules.CanTransition(current, target).Should().Be(expected);
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
    public async Task TransitionAsync_AllowedTransition_AtomicallyUpdatesAndCreatesStableOutboxMessage()
    {
        var fixture = CreateFixture(BookingStatuses.Pending);

        var result = await fixture.Service.TransitionAsync(
            Guid.NewGuid(), true, fixture.WorkOrderPublicId,
            BookingStatuses.Confirmed, "corr-status", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be(BookingStatuses.Confirmed);
        result.Sequence.Should().Be(1);
        var reservation = await fixture.Context.AppointmentReservations.SingleAsync();
        var workOrder = await fixture.Context.WorkOrders.SingleAsync();
        var outbox = await fixture.Context.BookingStatusOutboxMessages.SingleAsync();
        reservation.Status.Should().Be(BookingStatuses.Confirmed);
        workOrder.Status.Should().Be(BookingStatuses.Confirmed);
        outbox.Id.Should().Be(result.EventId);
        outbox.RequestJson.Should().Contain(result.EventId.ToString());
    }

    [Fact]
    public async Task TransitionAsync_SelfTransition_IsRejectedWithoutAnotherOutboxMessage()
    {
        var fixture = CreateFixture(BookingStatuses.Pending);
        var first = await fixture.Service.TransitionAsync(
            Guid.NewGuid(), true, fixture.WorkOrderPublicId,
            BookingStatuses.Confirmed, "corr-1", CancellationToken.None);

        var action = () => fixture.Service.TransitionAsync(
            Guid.NewGuid(), true, fixture.WorkOrderPublicId,
            BookingStatuses.Confirmed, "corr-2", CancellationToken.None);

        first.Should().NotBeNull();
        var exception = await action.Should().ThrowAsync<BookingStatusRejectedException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.TransitionInvalid);
        (await fixture.Context.BookingStatusOutboxMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TransitionAsync_BackwardTransition_RollsBackWithoutOutbox()
    {
        var fixture = CreateFixture(BookingStatuses.Completed);

        var action = () => fixture.Service.TransitionAsync(
            Guid.NewGuid(), true, fixture.WorkOrderPublicId,
            BookingStatuses.Confirmed, "corr", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<BookingStatusRejectedException>();
        exception.Which.Code.Should().Be(BookingStatusErrorCodes.TransitionInvalid);
        (await fixture.Context.BookingStatusOutboxMessages.CountAsync()).Should().Be(0);
        (await fixture.Context.WorkOrders.SingleAsync()).Status.Should().Be(BookingStatuses.Completed);
    }

    [Fact]
    public async Task TransitionAsync_AmbiguousSaveAfterPersistence_ReturnsCommittedOutboxEvent()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new AmbiguousSaveBusinessDbContext(options);
        var fixture = CreateFixture(BookingStatuses.Pending, context);
        context.ThrowAfterNextSave = true;

        var result = await fixture.Service.TransitionAsync(
            Guid.NewGuid(),
            true,
            fixture.WorkOrderPublicId,
            BookingStatuses.Confirmed,
            "corr-ambiguous",
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be(BookingStatuses.Confirmed);
        result.EventId.Should().NotBeEmpty();
        (await context.BookingStatusOutboxMessages.CountAsync()).Should().Be(1);
        (await context.AppointmentReservations.SingleAsync()).StatusSequence.Should().Be(1);
    }

    [Fact]
    public async Task TransitionAsync_SaveFailure_DoesNotAcknowledgePartialTransition()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FailingSaveBusinessDbContext(options);
        var fixture = CreateFixture(BookingStatuses.Pending, context);
        context.ThrowBeforeNextSave = true;

        var action = () => fixture.Service.TransitionAsync(
            Guid.NewGuid(), true, fixture.WorkOrderPublicId,
            BookingStatuses.Confirmed, "corr-fail", CancellationToken.None);

        await action.Should().ThrowAsync<DbUpdateException>();
        context.ChangeTracker.Clear();
        (await context.BookingStatusOutboxMessages.CountAsync()).Should().Be(0);
        (await context.WorkOrders.SingleAsync()).Status.Should().Be(BookingStatuses.Pending);
        (await context.AppointmentReservations.SingleAsync()).StatusSequence.Should().Be(0);
    }

    [Fact]
    public async Task TransitionAsync_SaveFailure_DoesNotAcknowledgeUnrelatedMatchingOutbox()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FailingSaveBusinessDbContext(options);
        var fixture = CreateFixture(BookingStatuses.Pending, context);
        var reservation = await context.AppointmentReservations.SingleAsync();
        context.BookingStatusOutboxMessages.Add(new BookingStatusOutboxMessage
        {
            Id = Guid.NewGuid(),
            AppointmentReservationId = reservation.Id,
            WorkOrderPublicId = fixture.WorkOrderPublicId,
            Status = BookingStatuses.Confirmed,
            Sequence = 1,
            RequestJson = "{}",
            RequestHash = new string('a', 64),
            CorrelationId = "older-event",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            NextAttemptAtUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.ThrowBeforeNextSave = true;

        var action = () => fixture.Service.TransitionAsync(
            Guid.NewGuid(), true, fixture.WorkOrderPublicId,
            BookingStatuses.Confirmed, "corr-fail-exact", CancellationToken.None);

        await action.Should().ThrowAsync<DbUpdateException>();
        (await context.BookingStatusOutboxMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TransitionAsync_LegacyReservedStatus_IsNormalizedToPending()
    {
        var fixture = CreateFixture("Reserved");

        var result = await fixture.Service.TransitionAsync(
            Guid.NewGuid(), true, fixture.WorkOrderPublicId,
            BookingStatuses.Confirmed, "corr-legacy", CancellationToken.None);

        result!.Status.Should().Be(BookingStatuses.Confirmed);
        result.Sequence.Should().Be(1);
    }

    [Theory]
    [InlineData(BusinessMembershipRole.Owner, true, false, true)]
    [InlineData(BusinessMembershipRole.Employee, true, true, true)]
    [InlineData(BusinessMembershipRole.Employee, true, false, false)]
    [InlineData(BusinessMembershipRole.Owner, false, false, false)]
    [InlineData(BusinessMembershipRole.Employee, false, true, false)]
    public async Task TransitionAsync_AssignmentScopeAndActivity_AuthorizesWithoutEnumeration(
        BusinessMembershipRole role,
        bool active,
        bool sameBranch,
        bool authorized)
    {
        var fixture = CreateFixture(BookingStatuses.Pending);
        var userId = Guid.NewGuid();
        fixture.Context.BusinessUserAssignments.Add(new BusinessUserAssignment
        {
            UserId = userId,
            CompanyId = fixture.CompanyId,
            BranchId = role == BusinessMembershipRole.Owner
                ? null
                : sameBranch ? fixture.BranchId : Guid.NewGuid(),
            Role = role,
            IsActive = active
        });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.TransitionAsync(
            userId, false, fixture.WorkOrderPublicId,
            BookingStatuses.Confirmed, "corr-auth", CancellationToken.None);

        if (authorized)
        {
            result.Should().NotBeNull();
        }
        else
        {
            result.Should().BeNull();
            (await fixture.Context.BookingStatusOutboxMessages.CountAsync()).Should().Be(0);
            (await fixture.Context.WorkOrders.SingleAsync()).Status
                .Should().Be(BookingStatuses.Pending);
        }
    }

    private static Fixture CreateFixture(
        string status,
        BusinessDbContext? suppliedContext = null)
    {
        var context = suppliedContext ?? new BusinessDbContext(
            new DbContextOptionsBuilder<BusinessDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var company = new Company { Id = Guid.NewGuid(), NameAr = "شركة" };
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "فرع",
            AddressAr = "عنوان"
        };
        var reservation = new AppointmentReservation
        {
            Id = Guid.NewGuid(),
            PublicId = Guid.NewGuid(),
            CustomerBookingReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            RequestHash = new('a', 64),
            BranchId = branch.Id,
            CatalogVersion = 1,
            Currency = "ILS",
            Status = status,
            StatusSequence = status is BookingStatuses.Pending or "Reserved" ? 0 : 3,
            StatusChangedAtUtc = DateTimeOffset.UtcNow,
            RequestedSlotStartUtc = DateTime.UtcNow,
            RequestedSlotEndUtc = DateTime.UtcNow.AddHours(1),
            WorkOrder = new WorkOrder
            {
                Id = Guid.NewGuid(),
                PublicId = Guid.NewGuid(),
                Status = status,
                CustomerName = "Customer",
                VehicleType = "Sedan",
                AddressLine = "Address"
            }
        };
        context.Branches.Add(branch);
        context.AppointmentReservations.Add(reservation);
        context.SaveChanges();
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(DateTime.UtcNow);
        var service = new BookingStatusService(
            context, clock.Object, Mock.Of<IAppLogger>());
        return new Fixture(
            context,
            service,
            reservation.WorkOrder.PublicId,
            company.Id,
            branch.Id);
    }

    private sealed record Fixture(
        BusinessDbContext Context,
        BookingStatusService Service,
        Guid WorkOrderPublicId,
        Guid CompanyId,
        Guid BranchId);

    private sealed class AmbiguousSaveBusinessDbContext : BusinessDbContext
    {
        public AmbiguousSaveBusinessDbContext(DbContextOptions<BusinessDbContext> options)
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
                throw new DbUpdateException("Simulated ambiguous save result.");
            }
            return result;
        }
    }

    private sealed class FailingSaveBusinessDbContext : BusinessDbContext
    {
        public FailingSaveBusinessDbContext(DbContextOptions<BusinessDbContext> options)
            : base(options)
        {
        }

        public bool ThrowBeforeNextSave { get; set; }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            if (ThrowBeforeNextSave)
            {
                ThrowBeforeNextSave = false;
                throw new DbUpdateException("Simulated failure before persistence.");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
