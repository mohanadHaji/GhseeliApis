using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.Common.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies durable, ordered, leased callback delivery and dead-letter recovery.
/// </summary>
public sealed class BookingStatusOutboxDispatcherTests
{
    [Fact]
    public async Task DeliverNextAsync_TransientFailure_RetriesWithStableIdentity()
    {
        var fixture = await CreateFixtureAsync(maxAttempts: 3);
        var message = await fixture.AddAsync(sequence: 1);
        var attempts = new List<(Guid EventId, string Key)>();
        fixture.Client.Setup(value => value.DeliverAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string, string, CancellationToken>(
                (_, id, key, _, _) => attempts.Add((id, key)))
            .Returns(() => attempts.Count == 1
                ? Task.FromException(new HttpRequestException("transient"))
                : Task.CompletedTask);

        (await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None)).Should().BeTrue();
        fixture.Clock.SetupGet(value => value.UtcNow).Returns(fixture.Now.AddMinutes(10));
        (await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None)).Should().BeTrue();

        attempts.Should().HaveCount(2);
        attempts.Select(value => value.EventId).Distinct().Should().ContainSingle()
            .Which.Should().Be(message.Id);
        attempts.Select(value => value.Key).Distinct().Should().ContainSingle()
            .Which.Should().Be($"booking-status-{message.Id:N}");
        var persisted = await fixture.Context.BookingStatusOutboxMessages.SingleAsync();
        persisted.DeliveryState.Should().Be(BookingStatusOutboxStates.Delivered);
        persisted.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task DeliverNextAsync_RequeuedGeneration_UsesNewStableIdentityAcrossRetries()
    {
        var fixture = await CreateFixtureAsync(maxAttempts: 3);
        var message = await fixture.AddAsync(sequence: 1);
        message.DeliveryGeneration = 1;
        await fixture.Context.SaveChangesAsync();
        var keys = new List<string>();
        fixture.Client.Setup(value => value.DeliverAsync(
                It.IsAny<string>(), message.Id, It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string, string, CancellationToken>(
                (_, _, key, _, _) => keys.Add(key))
            .Returns(() => keys.Count == 1
                ? Task.FromException(new HttpRequestException("transient"))
                : Task.CompletedTask);

        await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None);
        fixture.Clock.SetupGet(value => value.UtcNow).Returns(fixture.Now.AddMinutes(10));
        await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None);

        keys.Should().Equal(
            $"booking-status-{message.Id:N}-g1",
            $"booking-status-{message.Id:N}-g1");
    }

    [Fact]
    public async Task DeliverNextAsync_Permanent4xx_DeadLettersImmediately()
    {
        var fixture = await CreateFixtureAsync(maxAttempts: 5);
        await fixture.AddAsync(sequence: 1);
        fixture.Client.Setup(value => value.DeliverAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("bad request", null, HttpStatusCode.BadRequest));

        (await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None)).Should().BeTrue();

        var persisted = await fixture.Context.BookingStatusOutboxMessages.SingleAsync();
        persisted.DeliveryState.Should().Be(BookingStatusOutboxStates.DeadLetter);
        persisted.DeadLetteredAtUtc.Should().NotBeNull();
        persisted.LastErrorCode.Should().Be("HTTP_400");
    }

    [Fact]
    public async Task DeliverNextAsync_CachedGenerationZero4xx_RequeueGenerationOneCanSucceed()
    {
        var fixture = await CreateFixtureAsync(maxAttempts: 5);
        var message = await fixture.AddAsync(sequence: 1);
        var originalBody = message.RequestJson;
        var keys = new List<string>();
        fixture.Client.Setup(value => value.DeliverAsync(
                originalBody, message.Id, It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string, string, CancellationToken>(
                (_, _, key, _, _) => keys.Add(key))
            .Returns<string, Guid, string, string, CancellationToken>(
                (_, _, key, _, _) => key.EndsWith("-g1", StringComparison.Ordinal)
                    ? Task.CompletedTask
                    : Task.FromException(new HttpRequestException(
                        "cached bad request", null, HttpStatusCode.BadRequest)));

        await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None);
        var deadLetterService = new BookingStatusDeadLetterService(
            fixture.Context, fixture.Clock.Object, Mock.Of<IAppLogger>());
        var requeue = await deadLetterService.RequeueAsync(
            Guid.NewGuid(), message.Id, "operator-correction-1", CancellationToken.None);
        await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None);

        requeue!.Generation.Should().Be(1);
        keys.Should().Equal(
            $"booking-status-{message.Id:N}",
            $"booking-status-{message.Id:N}-g1");
        var persisted = await fixture.Context.BookingStatusOutboxMessages.SingleAsync();
        persisted.DeliveryState.Should().Be(BookingStatusOutboxStates.Delivered);
        persisted.RequestJson.Should().Be(originalBody);
        persisted.Sequence.Should().Be(message.Sequence);
        persisted.Status.Should().Be(message.Status);
    }

    [Fact]
    public async Task DeliverNextAsync_MaxAttempts_DeadLettersTransientFailure()
    {
        var fixture = await CreateFixtureAsync(maxAttempts: 2);
        await fixture.AddAsync(sequence: 1);
        fixture.Client.Setup(value => value.DeliverAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable));

        await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None);
        fixture.Clock.SetupGet(value => value.UtcNow).Returns(fixture.Now.AddMinutes(10));
        await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None);

        var persisted = await fixture.Context.BookingStatusOutboxMessages.SingleAsync();
        persisted.DeliveryState.Should().Be(BookingStatusOutboxStates.DeadLetter);
        persisted.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task DeliverNextAsync_HeadOfLineBackoff_BlocksLaterReservationEvent()
    {
        var fixture = await CreateFixtureAsync();
        var reservationId = Guid.NewGuid();
        var first = await fixture.AddAsync(1, reservationId);
        await fixture.AddAsync(2, reservationId);
        first.NextAttemptAtUtc = fixture.Now.AddMinutes(5);
        await fixture.Context.SaveChangesAsync();

        (await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None)).Should().BeFalse();
        fixture.Client.Verify(value => value.DeliverAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeliverNextAsync_DeadLetterBlocksLaterUntilRequeuedThenDeliversInSequence()
    {
        var fixture = await CreateFixtureAsync();
        var reservationId = Guid.NewGuid();
        var first = await fixture.AddAsync(1, reservationId);
        var second = await fixture.AddAsync(2, reservationId);
        first.DeliveryState = BookingStatusOutboxStates.DeadLetter;
        first.DeadLetteredAtUtc = fixture.Now;
        await fixture.Context.SaveChangesAsync();
        var delivered = new List<Guid>();
        fixture.Client.Setup(value => value.DeliverAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string, string, CancellationToken>(
                (_, eventId, _, _, _) => delivered.Add(eventId))
            .Returns(Task.CompletedTask);

        (await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None)).Should().BeFalse();
        delivered.Should().BeEmpty();

        var deadLetterService = new BookingStatusDeadLetterService(
            fixture.Context,
            fixture.Clock.Object,
            Mock.Of<IAppLogger>());
        (await deadLetterService.RequeueAsync(
            Guid.NewGuid(),
            first.Id,
            "dispatcher-ordering-test",
            CancellationToken.None))!.Outcome.Should().Be(DeadLetterRequeueOutcomes.Requeued);
        (await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None)).Should().BeTrue();
        (await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None)).Should().BeTrue();

        delivered.Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task DeliverNextAsync_ActiveLease_BlocksSecondDispatcher()
    {
        var database = Guid.NewGuid().ToString();
        var first = await CreateFixtureAsync(databaseName: database);
        await first.AddAsync(sequence: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Client.Setup(value => value.DeliverAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                started.SetResult();
                await release.Task;
            });
        var delivery = first.Dispatcher.DeliverNextAsync(CancellationToken.None);
        await started.Task;
        var second = await CreateFixtureAsync(databaseName: database);

        (await second.Dispatcher.DeliverNextAsync(CancellationToken.None)).Should().BeFalse();
        release.SetResult();
        (await delivery).Should().BeTrue();
    }

    [Fact]
    public async Task DeliverNextAsync_ExpiredLease_IsRecovered()
    {
        var fixture = await CreateFixtureAsync();
        var message = await fixture.AddAsync(sequence: 1);
        message.DeliveryState = BookingStatusOutboxStates.Leased;
        message.LeaseToken = Guid.NewGuid();
        message.LeaseOwner = "dead-replica";
        message.LeaseExpiresAtUtc = fixture.Now.AddSeconds(-1);
        await fixture.Context.SaveChangesAsync();

        (await fixture.Dispatcher.DeliverNextAsync(CancellationToken.None)).Should().BeTrue();
        (await fixture.Context.BookingStatusOutboxMessages.SingleAsync())
            .DeliveryState.Should().Be(BookingStatusOutboxStates.Delivered);
    }

    [Fact]
    public async Task DeliverNextAsync_LostLease_DoesNotOverwriteNewOwnerState()
    {
        var database = Guid.NewGuid().ToString();
        var fixture = await CreateFixtureAsync(databaseName: database);
        var message = await fixture.AddAsync(sequence: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.Setup(value => value.DeliverAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                started.SetResult();
                await release.Task;
            });
        var delivery = fixture.Dispatcher.DeliverNextAsync(CancellationToken.None);
        await started.Task;
        await using var other = new BusinessDbContext(
            new DbContextOptionsBuilder<BusinessDbContext>()
                .UseInMemoryDatabase(database)
                .Options);
        var stolen = await other.BookingStatusOutboxMessages.SingleAsync();
        var replacementToken = Guid.NewGuid();
        stolen.LeaseToken = replacementToken;
        stolen.LeaseOwner = "replacement";
        await other.SaveChangesAsync();

        release.SetResult();
        await delivery;

        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.BookingStatusOutboxMessages.SingleAsync();
        persisted.DeliveryState.Should().Be(BookingStatusOutboxStates.Leased);
        persisted.LeaseToken.Should().Be(replacementToken);
        persisted.DeliveredAtUtc.Should().BeNull();
        persisted.Id.Should().Be(message.Id);
    }

    private static async Task<Fixture> CreateFixtureAsync(
        int maxAttempts = 8,
        string? databaseName = null)
    {
        var now = DateTime.UtcNow;
        var context = new BusinessDbContext(
            new DbContextOptionsBuilder<BusinessDbContext>()
                .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
                .Options);
        var client = new Mock<ICustomerBookingStatusClient>();
        client.Setup(value => value.DeliverAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(now);
        var dispatcher = new BookingStatusOutboxDispatcher(
            context,
            client.Object,
            clock.Object,
            Mock.Of<IAppLogger>(),
            Options.Create(new BookingStatusOutboxOptions
            {
                LeaseSeconds = 60,
                MaxAttempts = maxAttempts,
                BaseRetrySeconds = 1,
                MaxRetrySeconds = 60
            }));
        await Task.CompletedTask;
        return new Fixture(context, client, clock, dispatcher, now);
    }

    private sealed record Fixture(
        BusinessDbContext Context,
        Mock<ICustomerBookingStatusClient> Client,
        Mock<ISystemClock> Clock,
        BookingStatusOutboxDispatcher Dispatcher,
        DateTime Now)
    {
        public async Task<BookingStatusOutboxMessage> AddAsync(
            long sequence,
            Guid? reservationId = null)
        {
            var message = new BookingStatusOutboxMessage
            {
                Id = Guid.NewGuid(),
                AppointmentReservationId = reservationId ?? Guid.NewGuid(),
                WorkOrderPublicId = Guid.NewGuid(),
                Status = "Confirmed",
                Sequence = sequence,
                RequestJson = "{}",
                RequestHash = new string('a', 64),
                CorrelationId = "corr",
                CreatedAtUtc = Now.AddSeconds(sequence),
                NextAttemptAtUtc = Now
            };
            Context.BookingStatusOutboxMessages.Add(message);
            await Context.SaveChangesAsync();
            return message;
        }
    }
}
