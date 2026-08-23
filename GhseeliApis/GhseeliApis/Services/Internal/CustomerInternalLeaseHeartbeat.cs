using Ghseeli.Common.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace GhseeliApis.Services.Internal;

internal sealed class CustomerInternalLeaseHeartbeat : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _leaseLost = new();
    private readonly Task _task;
    private long _lastConfirmedExpiryUtcTicks;

    private CustomerInternalLeaseHeartbeat(
        IServiceScopeFactory scopeFactory,
        Guid recordId,
        Guid ownerToken,
        DateTimeOffset initialExpiryUtc,
        CustomerInternalServiceOptions options,
        TimeProvider timeProvider,
        IAppLogger logger)
    {
        _lastConfirmedExpiryUtcTicks = initialExpiryUtc.UtcTicks;
        _task = RunAsync(
            scopeFactory,
            recordId,
            ownerToken,
            options,
            timeProvider,
            logger);
    }

    public CancellationToken LeaseLostToken => _leaseLost.Token;

    public DateTimeOffset LastConfirmedExpiryUtc =>
        new(Interlocked.Read(ref _lastConfirmedExpiryUtcTicks), TimeSpan.Zero);

    public static CustomerInternalLeaseHeartbeat Start(
        IServiceScopeFactory scopeFactory,
        Guid recordId,
        Guid ownerToken,
        DateTimeOffset initialExpiryUtc,
        CustomerInternalServiceOptions options,
        TimeProvider timeProvider,
        IAppLogger logger) =>
        new(
            scopeFactory,
            recordId,
            ownerToken,
            initialExpiryUtc,
            options,
            timeProvider,
            logger);

    public async Task StopAsync()
    {
        if (!_stop.IsCancellationRequested)
            await _stop.CancelAsync();
        await _task;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stop.Dispose();
        _leaseLost.Dispose();
    }

    private async Task RunAsync(
        IServiceScopeFactory scopeFactory,
        Guid recordId,
        Guid ownerToken,
        CustomerInternalServiceOptions options,
        TimeProvider timeProvider,
        IAppLogger logger)
    {
        var interval = TimeSpan.FromMilliseconds(
            options.InProgressRecoverySeconds *
            1000d *
            options.InProgressLeaseRenewalFraction);
        try
        {
            while (true)
            {
                await Task.Delay(interval, timeProvider, _stop.Token);
                if (!await RenewWithRetryAsync(
                        scopeFactory,
                        recordId,
                        ownerToken,
                        options,
                        timeProvider,
                        logger))
                {
                    await _leaseLost.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> RenewWithRetryAsync(
        IServiceScopeFactory scopeFactory,
        Guid recordId,
        Guid ownerToken,
        CustomerInternalServiceOptions options,
        TimeProvider timeProvider,
        IAppLogger logger)
    {
        var deadline = LastConfirmedExpiryUtc.AddMilliseconds(
            -options.InProgressLeaseSafetyMarginMilliseconds);
        while (timeProvider.GetUtcNow() < deadline && !_stop.IsCancellationRequested)
        {
            var remaining = deadline - timeProvider.GetUtcNow();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(CustomerInternalServiceMiddleware.Min(
                remaining,
                TimeSpan.FromMilliseconds(
                    options.InProgressLeaseOperationTimeoutMilliseconds)));
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var leaseService = scope.ServiceProvider
                    .GetRequiredService<ICustomerInternalIdempotencyLeaseService>();
                var expiry = await leaseService.RenewAsync(
                    recordId,
                    ownerToken,
                    timeout.Token);
                if (expiry is null)
                    return false;
                Interlocked.Exchange(
                    ref _lastConfirmedExpiryUtcTicks,
                    expiry.Value.UtcTicks);
                return true;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return true;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Customer internal idempotency lease renewal attempt failed.",
                    exception);
                remaining = deadline - timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                    break;
                await Task.Delay(
                    CustomerInternalServiceMiddleware.Min(
                        remaining,
                        TimeSpan.FromMilliseconds(
                            options.InProgressLeaseRetryBackoffMilliseconds)),
                    timeProvider,
                    _stop.Token);
            }
        }
        return _stop.IsCancellationRequested;
    }
}
