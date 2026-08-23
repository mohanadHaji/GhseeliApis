using FluentAssertions;
using GhseeliApis.Services.Business;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;

namespace GhseeliApis.Tests.Services;

/// <summary>
/// Defines cancellation behavior at Business API retry boundaries.
/// </summary>
public class BusinessApiResilienceDelegatingHandlerTests
{
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task SendAsync_WhenFirstRetryableResponseCancelsCaller_DoesNotRetry(
        HttpStatusCode statusCode)
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var downstream = new ScriptedHandler((_, _) =>
        {
            cancellationTokenSource.Cancel();
            return Task.FromResult(new HttpResponseMessage(statusCode));
        });
        using var client = CreateClient(downstream);

        var action = () => client.GetAsync(
            "api/v1/internal/catalog/companies/test",
            cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        downstream.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenFirstHttpRequestExceptionCancelsCaller_DoesNotRetry()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var downstream = new ScriptedHandler((_, _) =>
        {
            cancellationTokenSource.Cancel();
            throw new HttpRequestException("Connection failed.");
        });
        using var client = CreateClient(downstream);

        var action = () => client.GetAsync(
            "api/v1/internal/catalog/companies/test",
            cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        downstream.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenCallerIsCancelledDuringRetryDelay_DoesNotRetry()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var downstream = new ScriptedHandler((_, _) =>
        {
            cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(20));
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return Task.FromResult(response);
        });
        using var client = CreateClient(downstream);

        var action = () => client.GetAsync(
            "api/v1/internal/catalog/companies/test",
            cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        downstream.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenTimeoutPathAlsoCancelsCaller_PropagatesCallerCancellationWithoutRetry()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var downstream = new ScriptedHandler(async (_, attemptCancellationToken) =>
        {
            using var registration = attemptCancellationToken.Register(
                cancellationTokenSource.Cancel);
            await Task.Delay(Timeout.InfiniteTimeSpan, attemptCancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = CreateClient(downstream, timeoutSeconds: 0.02);

        var action = () => client.GetAsync(
            "api/v1/internal/catalog/companies/test",
            cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        downstream.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WithActiveToken_RetainsConfiguredRetryCount()
    {
        var downstream = new ScriptedHandler((sendCount, _) =>
            Task.FromResult(new HttpResponseMessage(
                sendCount < 3
                    ? HttpStatusCode.ServiceUnavailable
                    : HttpStatusCode.OK)));
        using var client = CreateClient(downstream, maxRetryAttempts: 2);

        using var response = await client.GetAsync(
            "api/v1/internal/catalog/companies/test",
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        downstream.SendCount.Should().Be(3);
    }

    private static HttpClient CreateClient(
        HttpMessageHandler innerHandler,
        int maxRetryAttempts = 1,
        double timeoutSeconds = 1)
    {
        var options = Options.Create(new BusinessApiClientOptions
        {
            TimeoutSeconds = timeoutSeconds,
            MaxRetryAttempts = maxRetryAttempts,
            MaxRetryAfterSeconds = 1
        });
        var resilienceHandler = new BusinessApiResilienceDelegatingHandler(options)
        {
            InnerHandler = innerHandler
        };

        return new HttpClient(resilienceHandler)
        {
            BaseAddress = new Uri("https://business.example.test"),
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<int, CancellationToken, Task<HttpResponseMessage>> _send;

        public ScriptedHandler(
            Func<int, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return _send(SendCount, cancellationToken);
        }
    }
}
