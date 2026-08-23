using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.Common.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies durable dead-letter requeue identity across worker lease transitions.
/// </summary>
public sealed class BookingStatusDeadLetterRelationalConcurrencyTests
{
    [Fact]
    public async Task RequeueAsync_WorkerLeasesThenDelivers_RepeatsRemainNoOpAndUnrelatedLeaseConflicts()
    {
        await using var database = await SqlServerBusinessDatabase.CreateAsync();
        var now = DateTime.UtcNow;
        var actorId = Guid.NewGuid();
        var eventId = await database.AddEventAsync(
            BookingStatusOutboxStates.DeadLetter, now);
        var enteredDelivery = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelivery = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Mock<ICustomerBookingStatusClient>();
        client.Setup(value => value.DeliverAsync(
                It.IsAny<string>(), eventId, It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                enteredDelivery.SetResult();
                await releaseDelivery.Task;
            });
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(now);

        await using (var context = database.CreateContext())
        {
            var service = new BookingStatusDeadLetterService(
                context, clock.Object, Mock.Of<IAppLogger>());

            var first = await service.RequeueAsync(
                actorId, eventId, "admin-request-1", CancellationToken.None);

            first!.Outcome.Should().Be(DeadLetterRequeueOutcomes.Requeued);
        }

        Task<bool> delivery;
        await using (var workerContext = database.CreateContext())
        {
            var dispatcher = CreateDispatcher(workerContext, client.Object, clock.Object);
            delivery = dispatcher.DeliverNextAsync(CancellationToken.None);
            await enteredDelivery.Task;

            var leasedBeforeRepeat = await database.ReadEventAsync(eventId);
            leasedBeforeRepeat.DeliveryState.Should().Be(BookingStatusOutboxStates.Leased);
            leasedBeforeRepeat.LeaseToken.Should().NotBeNull();

            await using var adminContext = database.CreateContext();
            var service = new BookingStatusDeadLetterService(
                adminContext, clock.Object, Mock.Of<IAppLogger>());

            var repeated = await service.RequeueAsync(
                Guid.NewGuid(), eventId, "admin-request-1", CancellationToken.None);

            repeated!.Outcome.Should().Be(DeadLetterRequeueOutcomes.AlreadyRequeued);
            repeated.DeliveryState.Should().Be(BookingStatusOutboxStates.Leased);
            var leasedAfterRepeat = await database.ReadEventAsync(eventId);
            leasedAfterRepeat.LeaseToken.Should().Be(leasedBeforeRepeat.LeaseToken);
            leasedAfterRepeat.LeaseOwner.Should().Be(leasedBeforeRepeat.LeaseOwner);
            leasedAfterRepeat.AttemptCount.Should().Be(leasedBeforeRepeat.AttemptCount);
            leasedAfterRepeat.Sequence.Should().Be(leasedBeforeRepeat.Sequence);
            leasedAfterRepeat.RequestHash.Should().Be(leasedBeforeRepeat.RequestHash);
            leasedAfterRepeat.CreatedAtUtc.Should().Be(leasedBeforeRepeat.CreatedAtUtc);
            leasedAfterRepeat.RequeuedAtUtc.Should().Be(new DateTimeOffset(now));
            leasedAfterRepeat.RequeuedByAdminUserId.Should().Be(actorId);
            leasedAfterRepeat.RequeueRequestId.Should().Be("admin-request-1");

            releaseDelivery.SetResult();
            (await delivery).Should().BeTrue();
        }

        var delivered = await database.ReadEventAsync(eventId);
        delivered.DeliveryState.Should().Be(BookingStatusOutboxStates.Delivered);
        delivered.AttemptCount.Should().Be(1);
        delivered.RequeuedAtUtc.Should().Be(new DateTimeOffset(now));
        delivered.RequeuedByAdminUserId.Should().Be(actorId);
        delivered.RequeueRequestId.Should().Be("admin-request-1");

        await using (var context = database.CreateContext())
        {
            var service = new BookingStatusDeadLetterService(
                context, clock.Object, Mock.Of<IAppLogger>());

            var repeated = await service.RequeueAsync(
                Guid.NewGuid(), eventId, "admin-request-1", CancellationToken.None);

            repeated!.Outcome.Should().Be(DeadLetterRequeueOutcomes.AlreadyRequeued);
            repeated.DeliveryState.Should().Be(BookingStatusOutboxStates.Delivered);
        }

        var unrelatedId = await database.AddEventAsync(
            BookingStatusOutboxStates.Leased, now.AddMinutes(1));
        await using (var context = database.CreateContext())
        {
            var service = new BookingStatusDeadLetterService(
                context, clock.Object, Mock.Of<IAppLogger>());

            var action = () => service.RequeueAsync(
                actorId, unrelatedId, "admin-request-4", CancellationToken.None);

            await action.Should().ThrowAsync<DeadLetterRequeueConflictException>();
        }
    }

    [Fact]
    public async Task RequeueAsync_ConcurrentSameRequest_IncrementsGenerationExactlyOnce()
    {
        await using var database = await SqlServerBusinessDatabase.CreateAsync();
        var now = DateTime.UtcNow;
        var eventId = await database.AddEventAsync(
            BookingStatusOutboxStates.DeadLetter, now);
        var actorId = Guid.NewGuid();
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(now);

        async Task<DeadLetterRequeueResponse?> RequeueAsync()
        {
            await using var context = database.CreateContext();
            return await new BookingStatusDeadLetterService(
                context, clock.Object, Mock.Of<IAppLogger>())
                .RequeueAsync(
                    actorId, eventId, "same-concurrent-request", CancellationToken.None);
        }

        var results = await Task.WhenAll(RequeueAsync(), RequeueAsync());

        results.Select(value => value!.Outcome).Should()
            .BeEquivalentTo(
                DeadLetterRequeueOutcomes.Requeued,
                DeadLetterRequeueOutcomes.AlreadyRequeued);
        results.Should().OnlyContain(value => value!.Generation == 1);
        var persisted = await database.ReadEventAsync(eventId);
        persisted.DeliveryGeneration.Should().Be(1);
        await using var readContext = database.CreateContext();
        (await readContext.BookingStatusRequeueHistory.CountAsync()).Should().Be(1);
    }

    private static BookingStatusOutboxDispatcher CreateDispatcher(
        BusinessDbContext context,
        ICustomerBookingStatusClient client,
        ISystemClock clock) =>
        new(
            context,
            client,
            clock,
            Mock.Of<IAppLogger>(),
            Options.Create(new BookingStatusOutboxOptions { LeaseSeconds = 60 }));

    private sealed class SqlServerBusinessDatabase : IAsyncDisposable
    {
        private readonly string _databaseName =
            $"GhseeliRequeueRace_{Guid.NewGuid():N}";

        public static async Task<SqlServerBusinessDatabase> CreateAsync()
        {
            var database = new SqlServerBusinessDatabase();
            await using var context = database.CreateContext();
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public BusinessDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<BusinessDbContext>()
                .UseSqlServer(ConnectionString)
                .Options);

        public async Task<Guid> AddEventAsync(string state, DateTime now)
        {
            await using var context = CreateContext();
            var reservation = new AppointmentReservation
            {
                CustomerBookingReference = Guid.NewGuid(),
                OrderGuid = Guid.NewGuid(),
                RequestHash = new string('b', 64),
                BranchId = Guid.NewGuid(),
                Currency = "ILS",
                RequestedSlotStartUtc = now,
                RequestedSlotEndUtc = now.AddHours(1),
                Status = "Confirmed",
                StatusSequence = 1,
                StatusChangedAtUtc = new DateTimeOffset(now),
                CreatedAtUtc = now
            };
            var message = new BookingStatusOutboxMessage
            {
                AppointmentReservation = reservation,
                WorkOrderPublicId = Guid.NewGuid(),
                Status = "Confirmed",
                Sequence = 1,
                RequestJson = "{}",
                RequestHash = new string('a', 64),
                CorrelationId = Guid.NewGuid().ToString("N"),
                DeliveryState = state,
                CreatedAtUtc = new DateTimeOffset(now),
                NextAttemptAtUtc = new DateTimeOffset(now),
                DeadLetteredAtUtc = state == BookingStatusOutboxStates.DeadLetter
                    ? new DateTimeOffset(now)
                    : null,
                LeaseToken = state == BookingStatusOutboxStates.Leased
                    ? Guid.NewGuid()
                    : null,
                LeaseOwner = state == BookingStatusOutboxStates.Leased
                    ? "unrelated-worker"
                    : null,
                LeaseExpiresAtUtc = state == BookingStatusOutboxStates.Leased
                    ? new DateTimeOffset(now.AddMinutes(1))
                    : null
            };
            context.Add(message);
            await context.SaveChangesAsync();
            return message.Id;
        }

        public async Task<BookingStatusOutboxMessage> ReadEventAsync(Guid eventId)
        {
            await using var context = CreateContext();
            return await context.BookingStatusOutboxMessages
                .AsNoTracking()
                .SingleAsync(value => value.Id == eventId);
        }

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
            $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }
}
