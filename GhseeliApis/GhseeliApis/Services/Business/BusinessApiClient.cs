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

    Task<CreateReservationResponse> CreateReservationAsync(
        CreateReservationRequest request,
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
    }

    public async Task<CatalogSnapshotResponse> GetCatalogSnapshotAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var correlationId = ResolveCorrelationId();
        var requestUri = ResolveRequestUri(
            $"/api/v1/internal/catalog/snapshot?companyId={companyId:D}",
            correlationId);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            requestUri);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.CorrelationIdHeaderName,
            correlationId);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorResponseAsync(response, correlationId, cancellationToken);
        }

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
        var requestUri = ResolveRequestUri(
            "/api/v1/internal/appointments/validate",
            correlationId);
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            requestUri)
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
        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorResponseAsync(response, correlationId, cancellationToken);
        }

        return await ReadResponseAsync<ValidateAppointmentResponse>(
            response,
            "appointment validation",
            correlationId,
            cancellationToken);
    }

    public async Task<CreateReservationResponse> CreateReservationAsync(
        CreateReservationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!InternalServiceHeaderValueValidator.IsValidIdempotencyKey(idempotencyKey))
        {
            throw new ArgumentException("The idempotency key is invalid.", nameof(idempotencyKey));
        }

        request.ContractVersion = BusinessCatalogContract.Version;
        var correlationId = ResolveCorrelationId();
        var requestUri = ResolveRequestUri("/api/v1/internal/reservations", correlationId);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
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
        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorResponseAsync(response, correlationId, cancellationToken);
        }

        return await ReadResponseAsync<CreateReservationResponse>(
            response,
            "reservation",
            correlationId,
            cancellationToken);
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

        var problem = TryReadProblem(content);
        var message = string.IsNullOrWhiteSpace(problem.Detail)
            ? $"Business API returned {(int)response.StatusCode}."
            : problem.Detail;

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new BusinessApiAuthenticationException(message, correlationId),
            HttpStatusCode.Conflict =>
                new BusinessApiConflictException(message, correlationId, problem.Code),
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound =>
                new BusinessApiContractException(message, correlationId),
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway
                or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError =>
                new BusinessApiUnavailableException(message, correlationId),
            _ => new BusinessApiContractException(message, correlationId)
        };
    }

    private string ResolveCorrelationId()
    {
        var currentCorrelationId = _httpContextAccessor.HttpContext?
            .Request
            .Headers[InternalServiceWireConstants.CorrelationIdHeaderName]
            .ToString();
        return InternalServiceHeaderValueValidator.GetOrCreateCorrelationId(currentCorrelationId);
    }

    private Uri ResolveRequestUri(
        string relativePath,
        string correlationId)
    {
        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new BusinessApiConfigurationException(
                "BusinessApiClient:BaseUrl must be an absolute URI before Business API calls can run.",
                correlationId);
        }

        if (_options.RequireHttps &&
            (!_environment.IsDevelopment() || !_options.AllowInsecureHttpInDevelopment) &&
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessApiConfigurationException(
                "BusinessApiClient:BaseUrl must use HTTPS unless development HTTP is explicitly allowed.",
                correlationId);
        }

        if (!InternalServiceHeaderValueValidator.IsValidServiceId(_options.ServiceId))
        {
            throw new BusinessApiConfigurationException(
                "BusinessApiClient:ServiceId is invalid.",
                correlationId);
        }

        if (string.IsNullOrWhiteSpace(_options.ActiveSecret) || _options.ActiveSecret.Length < 32)
        {
            throw new BusinessApiConfigurationException(
                "BusinessApiClient:ActiveSecret is invalid.",
                correlationId);
        }

        return new Uri(baseUri, relativePath);
    }

    private static (string? Code, string? Detail) TryReadProblem(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            var code = document.RootElement.TryGetProperty("code", out var codeElement)
                ? codeElement.GetString()
                : null;
            if (document.RootElement.TryGetProperty("detail", out var detail))
            {
                return (code, detail.GetString());
            }

            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return (code, message.GetString());
            }

            return (code, null);
        }
        catch (JsonException)
        {
        }

        return (null, null);
    }
}
