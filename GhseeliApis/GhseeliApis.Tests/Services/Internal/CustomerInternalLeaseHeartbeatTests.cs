using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Services.Internal;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace GhseeliApis.Tests.Services.Internal;

/// <summary>
/// Verifies retry, ownership-loss, and pacing behavior of the lease heartbeat.
/// </summary>
public sealed class CustomerInternalLeaseHeartbeatTests
{
    private static readonly TimeSpan MinimumExpectedRetrySpacing =
        TimeSpan.FromMilliseconds(35);

    [Fact]
    public async Task TransientRenewalFailure_RetriesThenRecoversBeforeDeadline()
    {
        var clock = new ManualHeartbeatTimeProvider(DateTimeOffset.UtcNow);
        var lease = new ScriptedLeaseService(
            clock,
            new InvalidOperationException("transient"),
            clock.GetUtcNow().AddSeconds(1));
        await using var provider = Provider(lease);
        var options = Options();
        await using var heartbeat = CustomerInternalLeaseHeartbeat.Start(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            clock.GetUtcNow().AddSeconds(1),
            options,
            clock,
            Mock.Of<IAppLogger>());

        await AdvanceNextTimerAsync(clock);
        await WaitUntilAsync(() => lease.RenewCalls == 1);
        await AdvanceNextTimerAsync(clock);
        await WaitUntilAsync(() => lease.RenewCalls >= 2);

        heartbeat.LeaseLostToken.IsCancellationRequested.Should().BeFalse();
        lease.MinimumCallSpacing.Should()
            .BeGreaterThanOrEqualTo(MinimumExpectedRetrySpacing);
    }

    [Fact]
    public async Task RepeatedRenewalFailures_CancelLeaseTokenWithoutBusyLoop()
    {
        var clock = new ManualHeartbeatTimeProvider(DateTimeOffset.UtcNow);
        var lease = new ScriptedLeaseService(
            clock,
            new InvalidOperationException("unavailable"));
        await using var provider = Provider(lease);
        var options = Options();
        await using var heartbeat = CustomerInternalLeaseHeartbeat.Start(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            clock.GetUtcNow().AddSeconds(1),
            options,
            clock,
            Mock.Of<IAppLogger>());
        var downstream = Task.Delay(Timeout.InfiniteTimeSpan, heartbeat.LeaseLostToken);

        for (var attempt = 0;
             attempt < 30 && !heartbeat.LeaseLostToken.IsCancellationRequested;
             attempt++)
        {
            if (!await AdvanceNextTimerAsync(
                    clock,
                    () => heartbeat.LeaseLostToken.IsCancellationRequested))
            {
                break;
            }
        }
        await WaitUntilAsync(() => heartbeat.LeaseLostToken.IsCancellationRequested);

        await FluentActions.Awaiting(() => downstream)
            .Should().ThrowAsync<OperationCanceledException>();
        lease.RenewCalls.Should().Be(16);
        lease.MinimumCallSpacing.Should()
            .Be(TimeSpan.FromMilliseconds(
                options.InProgressLeaseRetryBackoffMilliseconds));
    }

    [Fact]
    public async Task OwnershipChanged_StopsImmediatelyAndSignalsLeaseLoss()
    {
        var clock = new ManualHeartbeatTimeProvider(DateTimeOffset.UtcNow);
        var lease = new ScriptedLeaseService(clock, (DateTimeOffset?)null);
        await using var provider = Provider(lease);
        await using var heartbeat = CustomerInternalLeaseHeartbeat.Start(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            clock.GetUtcNow().AddSeconds(1),
            Options(),
            clock,
            Mock.Of<IAppLogger>());

        await AdvanceNextTimerAsync(clock);
        await WaitUntilAsync(() => heartbeat.LeaseLostToken.IsCancellationRequested);

        lease.RenewCalls.Should().Be(1);
    }

    private static CustomerInternalServiceOptions Options() => new()
    {
        InProgressRecoverySeconds = 1,
        InProgressLeaseRenewalFraction = 0.1,
        InProgressLeaseSafetyMarginMilliseconds = 100,
        InProgressLeaseOperationTimeoutMilliseconds = 100,
        InProgressLeaseRetryBackoffMilliseconds = 50
    };

    private static ServiceProvider Provider(ICustomerInternalIdempotencyLeaseService lease)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => lease);
        return services.BuildServiceProvider();
    }

    private static async Task<bool> AdvanceNextTimerAsync(
        ManualHeartbeatTimeProvider timeProvider,
        Func<bool>? completed = null)
    {
        await WaitUntilAsync(() =>
            timeProvider.HasScheduledTimer || completed?.Invoke() == true);
        if (completed?.Invoke() == true)
            return false;
        timeProvider.AdvanceToNextTimer();
        await Task.Yield();
        return true;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
        condition().Should().BeTrue();
    }

    private sealed class ScriptedLeaseService(
        TimeProvider timeProvider,
        params object?[] results) :
        ICustomerInternalIdempotencyLeaseService
    {
        private readonly Queue<object?> _results = new(results);
        private readonly List<long> _callTimestamps = [];
        private readonly object _sync = new();
        private int _renewCalls;
        public int RenewCalls => Volatile.Read(ref _renewCalls);

        public TimeSpan MinimumCallSpacing
        {
            get
            {
                lock (_sync)
                {
                    return _callTimestamps.Count < 2
                        ? TimeSpan.MaxValue
                        : _callTimestamps
                            .Zip(
                                _callTimestamps.Skip(1),
                                timeProvider.GetElapsedTime)
                            .Min();
                }
            }
        }

        public Task<DateTimeOffset?> RenewAsync(
            Guid recordId,
            Guid ownerToken,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _renewCalls);
            lock (_sync)
            {
                _callTimestamps.Add(timeProvider.GetTimestamp());
            }
            var result = _results.Count > 1 ? _results.Dequeue() : _results.Peek();
            return result is Exception exception
                ? Task.FromException<DateTimeOffset?>(exception)
                : Task.FromResult((DateTimeOffset?)result);
        }

        public Task<CustomerInternalIdempotencyClaim> ClaimAsync(
            string serviceId,
            string operation,
            string key,
            string requestHash,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> CompleteAsync(
            Guid recordId,
            Guid ownerToken,
            int statusCode,
            string contentType,
            byte[] responseBody,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ReleaseAsync(
            Guid recordId,
            Guid ownerToken,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ManualHeartbeatTimeProvider(DateTimeOffset utcNow) :
        TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public bool HasScheduledTimer
        {
            get
            {
                lock (_sync)
                {
                    return _timers.Any(timer => timer.IsScheduled);
                }
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _utcNow;
            }
        }

        public override long GetTimestamp()
        {
            lock (_sync)
            {
                return _timestamp;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_sync)
            {
                _timers.Add(timer);
                ChangeTimer(timer, dueTime, period);
            }
            return timer;
        }

        public void AdvanceToNextTimer()
        {
            ManualTimer[] dueTimers;
            lock (_sync)
            {
                var nextTimestamp = _timers
                    .Where(timer => timer.IsScheduled)
                    .Min(timer => timer.DueTimestamp);
                var elapsedTicks = nextTimestamp - _timestamp;
                _timestamp = nextTimestamp;
                _utcNow = _utcNow.AddTicks(elapsedTicks);
                dueTimers = _timers
                    .Where(timer =>
                        timer.IsScheduled &&
                        timer.DueTimestamp <= _timestamp)
                    .ToArray();
                foreach (var timer in dueTimers)
                {
                    timer.AdvanceAfterFiring();
                }
            }

            foreach (var timer in dueTimers)
            {
                timer.Fire();
            }
        }

        private bool ChangeTimer(
            ManualTimer timer,
            TimeSpan dueTime,
            TimeSpan period)
        {
            if (timer.IsDisposed)
                return false;
            timer.PeriodTicks = period == Timeout.InfiniteTimeSpan
                ? Timeout.Infinite
                : period.Ticks;
            timer.DueTimestamp = dueTime == Timeout.InfiniteTimeSpan
                ? long.MaxValue
                : checked(_timestamp + dueTime.Ticks);
            timer.IsScheduled = dueTime != Timeout.InfiniteTimeSpan;
            return true;
        }

        private void RemoveTimer(ManualTimer timer)
        {
            lock (_sync)
            {
                timer.IsDisposed = true;
                timer.IsScheduled = false;
                _timers.Remove(timer);
            }
        }

        private sealed class ManualTimer(
            ManualHeartbeatTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            public long DueTimestamp { get; set; }
            public long PeriodTicks { get; set; } = Timeout.Infinite;
            public bool IsScheduled { get; set; }
            public bool IsDisposed { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._sync)
                {
                    return owner.ChangeTimer(this, dueTime, period);
                }
            }

            public void Dispose() => owner.RemoveTimer(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void AdvanceAfterFiring()
            {
                if (PeriodTicks == Timeout.Infinite)
                {
                    IsScheduled = false;
                }
                else
                {
                    DueTimestamp = checked(DueTimestamp + PeriodTicks);
                }
            }

            public void Fire() => callback(state);
        }
    }
}
