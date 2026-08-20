using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GhseeliApis.Services.Business;

public interface IBusinessApiClient
{
    Task<CatalogSnapshotResponse> GetCatalogSnapshotAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task<ValidateAppointmentResponse> ValidateAppointmentAsync(
        ValidateAppointmentRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed class BusinessApiClient : IBusinessApiClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    private readonly HttpClient _httpClient;
    private readonly BusinessApiClientOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BusinessApiClient(
        HttpClient httpClient,
        IOptions<BusinessApiClientOptions> options,
        IWebHostEnvironment environment,
        IHttpContextAccessor httpContextAccessor)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _environment = environment;
        _httpContextAccessor = httpContextAccessor;

        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("BusinessApiClient:BaseUrl must be an absolute URI.");
        }

        if (_options.RequireHttps &&
            (!environment.IsDevelopment() || !_options.AllowInsecureHttpInDevelopment) &&
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "BusinessApiClient:BaseUrl must use HTTPS unless development HTTP is explicitly allowed.");
        }
    }

    public async Task<CatalogSnapshotResponse> GetCatalogSnapshotAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var correlationId = ResolveCorrelationId();

        using var response = await SendWithRetriesAsync(
            () =>
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/api/v1/internal/catalog/snapshot?companyId={companyId:D}");
                request.Headers.TryAddWithoutValidation(
                    InternalServiceWireConstants.CorrelationIdHeaderName,
                    correlationId);
                return request;
            },
            correlationId,
            cancellationToken);

        return await ReadResponseAsync<CatalogSnapshotResponse>(
            response,
            "catalog snapshot",
            correlationId,
            cancellationToken);
    }

    public async Task<ValidateAppointmentResponse> ValidateAppointmentAsync(
        ValidateAppointmentRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!InternalServiceHeaderValueValidator.IsValidIdempotencyKey(idempotencyKey))
        {
            throw new ArgumentException("The idempotency key is invalid.", nameof(idempotencyKey));
        }

        request.ContractVersion = BusinessCatalogContract.Version;
        var correlationId = ResolveCorrelationId();

        using var response = await SendWithRetriesAsync(
            () =>
            {
                var httpRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    "/api/v1/internal/appointments/validate")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(request, JsonOptions),
                        Encoding.UTF8,
                        "application/json")
                };
                httpRequest.Headers.TryAddWithoutValidation(
                    InternalServiceWireConstants.IdempotencyKeyHeaderName,
                    idempotencyKey);
                httpRequest.Headers.TryAddWithoutValidation(
                    InternalServiceWireConstants.CorrelationIdHeaderName,
                    correlationId);
                return httpRequest;
            },
            correlationId,
            cancellationToken);

        return await ReadResponseAsync<ValidateAppointmentResponse>(
            response,
            "appointment validation",
            correlationId,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithRetriesAsync(
        Func<HttpRequestMessage> createRequest,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (true)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var request = createRequest();

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                if (attempt < _options.MaxRetryAttempts)
                {
                    attempt++;
                    continue;
                }

                throw new BusinessApiTimeoutException(
                    "The Business API request timed out.",
                    correlationId,
                    exception);
            }
            catch (HttpRequestException exception)
            {
                if (attempt < _options.MaxRetryAttempts)
                {
                    attempt++;
                    continue;
                }

                throw new BusinessApiUnavailableException(
                    "The Business API request could not be completed.",
                    correlationId,
                    exception);
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            if (IsRetryable(response.StatusCode) && attempt < _options.MaxRetryAttempts)
            {
                var delay = GetRetryDelay(response);
                response.Dispose();
                attempt++;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                continue;
            }

            await ThrowForErrorResponseAsync(response, correlationId, cancellationToken);
        }
    }

    private async Task<TResponse> ReadResponseAsync<TResponse>(
        HttpResponseMessage response,
        string operationName,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new BusinessApiContractException(
                $"The Business API {operationName} response was empty.",
                correlationId);
        }

        try
        {
            var result = JsonSerializer.Deserialize<TResponse>(content, JsonOptions);
            return result ?? throw new JsonException("The payload was null.");
        }
        catch (JsonException exception)
        {
            throw new BusinessApiContractException(
                $"The Business API {operationName} response did not match the expected contract.",
                correlationId,
                exception);
        }
    }

    private async Task ThrowForErrorResponseAsync(
        HttpResponseMessage response,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var content = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        var detail = TryReadProblemDetail(content);
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"Business API returned {(int)response.StatusCode}."
            : detail;

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new BusinessApiAuthenticationException(message, correlationId),
            HttpStatusCode.Conflict =>
                new BusinessApiConflictException(message, correlationId),
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound =>
                new BusinessApiContractException(message, correlationId),
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway
                or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError =>
                new BusinessApiUnavailableException(message, correlationId),
            _ => new BusinessApiContractException(message, correlationId)
        };
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta)
        {
            return CapRetryAfter(delta);
        }

        if (retryAfter?.Date is DateTimeOffset retryDate)
        {
            return CapRetryAfter(retryDate - DateTimeOffset.UtcNow);
        }

        return TimeSpan.Zero;
    }

    private TimeSpan CapRetryAfter(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var maximum = TimeSpan.FromSeconds(Math.Max(_options.MaxRetryAfterSeconds, 0));
        return delay > maximum ? maximum : delay;
    }

    private static bool IsRetryable(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.TooManyRequests ||
               (int)statusCode >= 500;
    }

    private string ResolveCorrelationId()
    {
        var currentCorrelationId = _httpContextAccessor.HttpContext?
            .Request
            .Headers[InternalServiceWireConstants.CorrelationIdHeaderName]
            .ToString();
        return InternalServiceHeaderValueValidator.GetOrCreateCorrelationId(currentCorrelationId);
    }

    private static string? TryReadProblemDetail(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.GetString();
            }

            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
