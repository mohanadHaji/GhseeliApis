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
    [Fact]
    public async Task TransientRenewalFailure_RetriesThenRecoversBeforeDeadline()
    {
        var lease = new ScriptedLeaseService(
            new InvalidOperationException("transient"),
            DateTimeOffset.UtcNow.AddSeconds(1));
        await using var provider = Provider(lease);
        var options = Options();
        await using var heartbeat = CustomerInternalLeaseHeartbeat.Start(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddSeconds(1),
            options,
            TimeProvider.System,
            Mock.Of<IAppLogger>());

        await WaitUntilAsync(() => lease.RenewCalls >= 2);

        heartbeat.LeaseLostToken.IsCancellationRequested.Should().BeFalse();
        lease.MinimumCallSpacing.Should()
            .BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(35));
    }

    [Fact]
    public async Task RepeatedRenewalFailures_CancelLeaseTokenWithoutBusyLoop()
    {
        var lease = new ScriptedLeaseService(new InvalidOperationException("unavailable"));
        await using var provider = Provider(lease);
        var options = Options();
        await using var heartbeat = CustomerInternalLeaseHeartbeat.Start(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddSeconds(1),
            options,
            TimeProvider.System,
            Mock.Of<IAppLogger>());
        var downstream = Task.Delay(Timeout.InfiniteTimeSpan, heartbeat.LeaseLostToken);

        await WaitUntilAsync(() => heartbeat.LeaseLostToken.IsCancellationRequested);

        await FluentActions.Awaiting(() => downstream)
            .Should().ThrowAsync<OperationCanceledException>();
        lease.RenewCalls.Should().BeInRange(2, 20);
        lease.MinimumCallSpacing.Should()
            .BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(35));
    }

    [Fact]
    public async Task OwnershipChanged_StopsImmediatelyAndSignalsLeaseLoss()
    {
        var lease = new ScriptedLeaseService((DateTimeOffset?)null);
        await using var provider = Provider(lease);
        await using var heartbeat = CustomerInternalLeaseHeartbeat.Start(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddSeconds(1),
            Options(),
            TimeProvider.System,
            Mock.Of<IAppLogger>());

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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
        condition().Should().BeTrue();
    }

    private sealed class ScriptedLeaseService(params object?[] results) :
        ICustomerInternalIdempotencyLeaseService
    {
        private readonly Queue<object?> _results = new(results);
        private readonly List<DateTimeOffset> _callTimes = [];
        public int RenewCalls { get; private set; }

        public TimeSpan MinimumCallSpacing => _callTimes.Count < 2
            ? TimeSpan.MaxValue
            : _callTimes.Zip(_callTimes.Skip(1), (first, second) => second - first).Min();

        public Task<DateTimeOffset?> RenewAsync(
            Guid recordId,
            Guid ownerToken,
            CancellationToken cancellationToken)
        {
            RenewCalls++;
            _callTimes.Add(DateTimeOffset.UtcNow);
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
}
