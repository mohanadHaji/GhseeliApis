using FluentAssertions;
using Ghseeli.BusinessApi.DataPartitioning;
using Ghseeli.BusinessApi.Services;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.DataPartitioning;
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

    [Fact]
    public async Task Worker_DispatchesProductionAndDemoPartitionsInSeparateScopes()
    {
        var observed = new List<string>();
        var bothObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddScoped<IBusinessDataPartitionContext, BusinessDataPartitionContext>();
        services.AddScoped<IBookingStatusOutboxDispatcher>(provider =>
            new PartitionRecordingDispatcher(
                provider.GetRequiredService<IBusinessDataPartitionContext>(),
                observed,
                bothObserved));
        using var provider = services.BuildServiceProvider();
        var worker = new BookingStatusOutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CustomerBookingStatusClientOptions { BaseUrl = "https://customer.test" }),
            Options.Create(new BookingStatusOutboxOptions
            {
                PollIntervalMilliseconds = 5
            }),
            Mock.Of<IAppLogger>());

        await worker.StartAsync(CancellationToken.None);
        await bothObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        observed.Should().ContainInOrder(
            DataPartitionNames.Production,
            DataPartitionNames.Demo);
    }

    private static ServiceProvider CreateProvider(IBookingStatusOutboxDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddScoped<IBusinessDataPartitionContext, BusinessDataPartitionContext>();
        services.AddScoped(_ => dispatcher);
        return services.BuildServiceProvider();
    }

    private sealed class PartitionRecordingDispatcher(
        IBusinessDataPartitionContext partition,
        List<string> observed,
        TaskCompletionSource bothObserved) : IBookingStatusOutboxDispatcher
    {
        public Task<bool> DeliverNextAsync(CancellationToken cancellationToken)
        {
            lock (observed)
            {
                observed.Add(partition.Partition);
                if (observed.Contains(DataPartitionNames.Production) &&
                    observed.Contains(DataPartitionNames.Demo))
                {
                    bothObserved.TrySetResult();
                }
            }

            return Task.FromResult(false);
        }
    }
}
