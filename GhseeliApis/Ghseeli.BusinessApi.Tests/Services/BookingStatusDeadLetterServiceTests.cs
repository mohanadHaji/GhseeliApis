using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.Common.Logging;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies audited, idempotent administrative dead-letter recovery.
/// </summary>
public sealed class BookingStatusDeadLetterServiceTests
{
    [Theory]
    [InlineData(BookingStatusOutboxStates.Pending)]
    [InlineData(BookingStatusOutboxStates.Delivered)]
    public async Task RequeueAsync_NeverRequeuedPendingOrDeliveredEvent_ReturnsConflict(
        string state)
    {
        var fixture = await CreateFixtureAsync(state);

        var action = () => fixture.Service.RequeueAsync(
            fixture.ActorId, fixture.EventId, "request-repeat", CancellationToken.None);

        await action.Should().ThrowAsync<DeadLetterRequeueConflictException>();
    }

    [Fact]
    public async Task RequeueAsync_DeadLetter_RequeuesAndAuditsAdminActor()
    {
        var fixture = await CreateFixtureAsync(BookingStatusOutboxStates.DeadLetter);
        var result = await fixture.Service.RequeueAsync(
            fixture.ActorId, fixture.EventId, "request-first", CancellationToken.None);

        result!.Outcome.Should().Be(DeadLetterRequeueOutcomes.Requeued);
        result.Generation.Should().Be(1);
        var persisted = await fixture.Context.BookingStatusOutboxMessages.SingleAsync();
        persisted.DeliveryGeneration.Should().Be(1);
        persisted.RequeuedAtUtc.Should().Be(fixture.Now);
        persisted.RequeuedByAdminUserId.Should().Be(fixture.ActorId);
        persisted.RequeueRequestId.Should().Be("request-first");
        fixture.Logger.Verify(value => value.LogInfo(It.Is<string>(message =>
            message.Contains(fixture.ActorId.ToString()) &&
            message.Contains(fixture.EventId.ToString()) &&
            message.Contains(DeadLetterRequeueOutcomes.Requeued))), Times.Once);
    }

    [Fact]
    public async Task RequeueAsync_SameRequest_AuditsNoOpWithoutChangingPersistentAudit()
    {
        var fixture = await CreateFixtureAsync(BookingStatusOutboxStates.DeadLetter);
        await fixture.Service.RequeueAsync(
            fixture.ActorId, fixture.EventId, "request-first", CancellationToken.None);
        var persistedAfterFirst = await fixture.Context.BookingStatusOutboxMessages
            .AsNoTracking()
            .SingleAsync();
        var replayActorId = Guid.NewGuid();

        var result = await fixture.Service.RequeueAsync(
            replayActorId, fixture.EventId, "request-first", CancellationToken.None);

        result!.Outcome.Should().Be(DeadLetterRequeueOutcomes.AlreadyRequeued);
        var persistedAfterRepeat = await fixture.Context.BookingStatusOutboxMessages
            .AsNoTracking()
            .SingleAsync();
        persistedAfterRepeat.RequeuedAtUtc.Should().Be(persistedAfterFirst.RequeuedAtUtc);
        persistedAfterRepeat.RequeuedByAdminUserId.Should()
            .Be(persistedAfterFirst.RequeuedByAdminUserId);
        persistedAfterRepeat.RequeueRequestId.Should().Be("request-first");
        persistedAfterRepeat.DeliveryGeneration.Should().Be(1);
        fixture.Logger.Verify(value => value.LogInfo(It.Is<string>(message =>
            message.Contains(replayActorId.ToString()) &&
            message.Contains(fixture.EventId.ToString()) &&
            message.Contains(DeadLetterRequeueOutcomes.AlreadyRequeued))), Times.Once);
    }

    [Fact]
    public async Task RequeueAsync_SecondDeadLetterWithNewRequest_IncrementsGenerationAgain()
    {
        var fixture = await CreateFixtureAsync(BookingStatusOutboxStates.DeadLetter);
        await fixture.Service.RequeueAsync(
            fixture.ActorId, fixture.EventId, "request-first", CancellationToken.None);
        var message = await fixture.Context.BookingStatusOutboxMessages.SingleAsync();
        message.DeliveryState = BookingStatusOutboxStates.DeadLetter;
        message.DeadLetteredAtUtc = fixture.Now.AddMinutes(1);
        await fixture.Context.SaveChangesAsync();

        var second = await fixture.Service.RequeueAsync(
            fixture.ActorId, fixture.EventId, "request-second", CancellationToken.None);
        var repeatedFirst = await fixture.Service.RequeueAsync(
            fixture.ActorId, fixture.EventId, "request-first", CancellationToken.None);

        second!.Outcome.Should().Be(DeadLetterRequeueOutcomes.Requeued);
        second.Generation.Should().Be(2);
        repeatedFirst!.Outcome.Should().Be(DeadLetterRequeueOutcomes.AlreadyRequeued);
        repeatedFirst.Generation.Should().Be(1);
        (await fixture.Context.BookingStatusRequeueHistory.AsNoTracking().ToListAsync())
            .Select(value => value.Generation).Should().Equal(1, 2);
    }

    [Fact]
    public async Task RequeueAsync_UnknownId_ReturnsNullWithoutEnumerationDetails()
    {
        var fixture = await CreateFixtureAsync(BookingStatusOutboxStates.DeadLetter);

        var result = await fixture.Service.RequeueAsync(
            fixture.ActorId, Guid.NewGuid(), "request-unknown", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RequeueAsync_LeasedEvent_ReturnsConflictAndDoesNotMutate()
    {
        var fixture = await CreateFixtureAsync(BookingStatusOutboxStates.Leased);

        var action = () => fixture.Service.RequeueAsync(
            fixture.ActorId, fixture.EventId, "request-leased", CancellationToken.None);

        await action.Should().ThrowAsync<DeadLetterRequeueConflictException>();
    }

    private static async Task<Fixture> CreateFixtureAsync(string state)
    {
        var context = new BusinessDbContext(new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var reservation = new AppointmentReservation
        {
            Id = Guid.NewGuid(),
            PublicId = Guid.NewGuid(),
            CustomerBookingReference = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            Status = "Confirmed"
        };
        var eventId = Guid.NewGuid();
        context.BookingStatusOutboxMessages.Add(new BookingStatusOutboxMessage
        {
            Id = eventId,
            AppointmentReservation = reservation,
            AppointmentReservationId = reservation.Id,
            WorkOrderPublicId = Guid.NewGuid(),
            Status = "Confirmed",
            Sequence = 1,
            RequestJson = "{}",
            RequestHash = new string('a', 64),
            CorrelationId = "corr",
            DeliveryState = state,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            NextAttemptAtUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        var logger = new Mock<IAppLogger>();
        var clock = new Mock<ISystemClock>();
        var now = DateTime.UtcNow;
        clock.SetupGet(value => value.UtcNow).Returns(now);
        var actorId = Guid.NewGuid();
        return new Fixture(
            context,
            logger,
            new BookingStatusDeadLetterService(
                context, clock.Object, logger.Object),
            actorId,
            eventId,
            now);
    }

    private sealed record Fixture(
        BusinessDbContext Context,
        Mock<IAppLogger> Logger,
        BookingStatusDeadLetterService Service,
        Guid ActorId,
        Guid EventId,
        DateTimeOffset Now);
}
