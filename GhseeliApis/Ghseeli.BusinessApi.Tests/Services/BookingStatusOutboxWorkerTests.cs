using FluentAssertions;
using Ghseeli.BusinessApi.Services;
using Ghseeli.Common.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies the hosted outbox loop remains healthy after transient cycle failures.
/// </summary>
public sealed class BookingStatusOutboxWorkerTests
{
    [Fact]
    public async Task Worker_WhenCycleFails_ContinuesAndDispatchesSubsequentEvent()
    {
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new Mock<IBookingStatusOutboxDispatcher>();
        var call = 0;
        dispatcher.Setup(value => value.DeliverNextAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                call++;
                if (call == 1)
                {
                    return Task.FromException<bool>(
                        new InvalidOperationException("database unavailable"));
                }
                if (call == 2)
                {
                    dispatched.TrySetResult();
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            });
        using var provider = CreateProvider(dispatcher.Object);
        var logger = new Mock<IAppLogger>();
        var worker = new BookingStatusOutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CustomerBookingStatusClientOptions { BaseUrl = "https://customer.test" }),
            Options.Create(new BookingStatusOutboxOptions
            {
                PollIntervalMilliseconds = 5,
                FailureBackoffMilliseconds = 5
            }),
            logger.Object);

        await worker.StartAsync(CancellationToken.None);
        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        dispatcher.Verify(value => value.DeliverNextAsync(It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
        logger.Verify(value => value.LogError(
            It.Is<string>(message => !message.Contains("database unavailable"))),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Worker_WhenCancelled_StopsWithoutHostFatalException()
    {
        var dispatcher = new Mock<IBookingStatusOutboxDispatcher>();
        dispatcher.Setup(value => value.DeliverNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        using var provider = CreateProvider(dispatcher.Object);
        var worker = new BookingStatusOutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CustomerBookingStatusClientOptions { BaseUrl = "https://customer.test" }),
            Options.Create(new BookingStatusOutboxOptions
            {
                PollIntervalMilliseconds = 5,
                FailureBackoffMilliseconds = 5
            }),
            Mock.Of<IAppLogger>());

        await worker.StartAsync(CancellationToken.None);
        var stop = () => worker.StopAsync(CancellationToken.None);

        await stop.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Worker_WhenExplicitlyDisabledForTesting_DoesNotResolveDispatcher()
    {
        var dispatcher = new Mock<IBookingStatusOutboxDispatcher>();
        using var provider = CreateProvider(dispatcher.Object);
        var worker = new BookingStatusOutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CustomerBookingStatusClientOptions
            {
                DisableDeliveryInTesting = true
            }),
            Options.Create(new BookingStatusOutboxOptions
            {
                PollIntervalMilliseconds = 5
            }),
            Mock.Of<IAppLogger>());

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(30);
        await worker.StopAsync(CancellationToken.None);

        dispatcher.Verify(
            value => value.DeliverNextAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ServiceProvider CreateProvider(IBookingStatusOutboxDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => dispatcher);
        return services.BuildServiceProvider();
    }
}
