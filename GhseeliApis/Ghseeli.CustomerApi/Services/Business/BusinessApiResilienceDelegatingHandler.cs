using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.Extensions.Options;
using System.Net;

namespace GhseeliApis.Services.Business;

public sealed class BusinessApiResilienceDelegatingHandler : DelegatingHandler
{
    private readonly IOptions<BusinessApiClientOptions> _options;

    public BusinessApiResilienceDelegatingHandler(
        IOptions<BusinessApiClientOptions> options)
    {
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var correlationId = request.Headers.TryGetValues(
            InternalServiceWireConstants.CorrelationIdHeaderName,
            out var values)
            ? values.SingleOrDefault()
            : null;

        var attempt = 0;
        while (true)
        {
            using var timeoutCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellationTokenSource.CancelAfter(
                TimeSpan.FromSeconds(options.TimeoutSeconds));
            using var clonedRequest = await CloneAsync(request, cancellationToken);

            HttpResponseMessage response;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                response = await base.SendAsync(
                    clonedRequest,
                    timeoutCancellationTokenSource.Token);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested &&
                      timeoutCancellationTokenSource.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt < options.MaxRetryAttempts)
                {
                    attempt++;
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                throw new BusinessApiTimeoutException(
                    "The Business API request timed out.",
                    correlationId,
                    exception);
            }
            catch (HttpRequestException exception)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt < options.MaxRetryAttempts)
                {
                    attempt++;
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                throw new BusinessApiUnavailableException(
                    "The Business API request could not be completed.",
                    correlationId,
                    exception);
            }

            if (!IsRetryable(response.StatusCode) || attempt >= options.MaxRetryAttempts)
            {
                return response;
            }

            var delay = GetRetryDelay(response, options.MaxRetryAfterSeconds);
            response.Dispose();
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var payload = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(payload);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int maxRetryAfterSeconds)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta)
        {
            return CapRetryAfter(delta, maxRetryAfterSeconds);
        }

        if (retryAfter?.Date is DateTimeOffset date)
        {
            return CapRetryAfter(date - DateTimeOffset.UtcNow, maxRetryAfterSeconds);
        }

        return TimeSpan.Zero;
    }

    private static TimeSpan CapRetryAfter(
        TimeSpan delay,
        int maxRetryAfterSeconds)
    {
        if (delay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var maximum = TimeSpan.FromSeconds(Math.Max(maxRetryAfterSeconds, 0));
        return delay > maximum ? maximum : delay;
    }
}
