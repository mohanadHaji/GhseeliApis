using FluentAssertions;
using Ghseeli.BusinessApi.InternalServices;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.BusinessApi.Tests.Integration;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Verifies behavior-safe rate limiting for invalid and valid internal HMAC traffic.
/// </summary>
public sealed class BusinessInternalRateLimitingIntegrationTests : IClassFixture<CatalogApiFactory>
{
    private const string WrongSecret = "Step17WrongSecret_Minimum32Characters";
    private const string RequestMarker = "step17-private-request-marker";
    private readonly CatalogApiFactory _factory;

    public BusinessInternalRateLimitingIntegrationTests(CatalogApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-RATE-013")]
    public async Task Step17SecRate013_InvalidHmacAttempts_AreLimitedByPeerWithoutSideEffects()
    {
        _factory.ResetState();
        using var rateLimitedFactory = CreateRateLimitedFactory();
        using var client = rateLimitedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var responses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = await CreateAppointmentRequestAsync(
                client,
                $"step17-rate-013-{attempt}",
                $"corr-step17-rate-013-{attempt}",
                WrongSecret);
            responses.Add(await client.SendAsync(request));
        }

        foreach (var pair in responses.Take(3).Select((response, index) => (response, index)))
        {
            await AssertMachineProblemAsync(
                pair.response,
                HttpStatusCode.Unauthorized,
                InternalServiceProblemCodes.InvalidSignature,
                "Internal service request was rejected.",
                "The supplied signature is invalid.");
            AssertTransportHeaders(
                pair.response,
                $"corr-step17-rate-013-{pair.index}");
        }

        await AssertRateLimitProblemAsync(responses[3], "corr-step17-rate-013-3");

        using var scope = rateLimitedFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        (await dbContext.InternalServiceNonces.CountAsync()).Should().Be(0);
        (await dbContext.InternalServiceIdempotencyRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-SEC-RATE-014")]
    public async Task Step17SecRate014_ValidIdempotentRetries_DoNotConsumeInvalidHmacBucket()
    {
        _factory.ResetState();
        using var rateLimitedFactory = CreateRateLimitedFactory(useTransientIdempotencyStore: true);
        using var client = rateLimitedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var invalidRequest = await CreateAppointmentRequestAsync(
                client,
                $"step17-rate-014-invalid-{attempt}",
                $"corr-step17-rate-014-invalid-{attempt}",
                WrongSecret);
            using var invalidResponse = await client.SendAsync(invalidRequest);
            invalidResponse.StatusCode.Should().Be(
                attempt < 3 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests);
        }

        const string idempotencyKey = "step17-rate-014-valid-retry";
        const string correlationId = "corr-step17-rate-014-valid";
        var validResponses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var validRequest = await CreateAppointmentRequestAsync(
                client,
                idempotencyKey,
                correlationId,
                CatalogApiFactory.InternalServiceActiveSecret);
            validResponses.Add(await client.SendAsync(validRequest));
        }

        foreach (var response in validResponses.Take(2))
        {
            await AssertMachineProblemAsync(
                response,
                HttpStatusCode.ServiceUnavailable,
                InternalServiceProblemCodes.IdempotencyUnavailable,
                "Idempotent request result is unavailable.",
                "The original request is still in progress. Retry with the same idempotency key later.");
            AssertTransportHeaders(response, correlationId);
        }

        validResponses[2].StatusCode.Should().Be(HttpStatusCode.OK);
        validResponses[2].Headers.CacheControl?.NoStore.Should().BeTrue();
        AssertTransportHeaders(validResponses[2], correlationId);
        var successBody = await validResponses[2].Content.ReadAsStringAsync();
        successBody.Should().NotContain(RequestMarker);
        successBody.Should().NotContain(CatalogApiFactory.InternalServiceActiveSecret);

        var idempotencyStore =
            rateLimitedFactory.Services.GetRequiredService<TransientUnavailableIdempotencyStore>();
        idempotencyStore.ClaimCount.Should().Be(3);
        idempotencyStore.CompletionCount.Should().Be(1);
        var validationService =
            rateLimitedFactory.Services.GetRequiredService<CountingAppointmentValidationService>();
        validationService.InvocationCount.Should().Be(1);

        using var scope = rateLimitedFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        (await dbContext.InternalServiceNonces.CountAsync()).Should().Be(3);
        (await dbContext.InternalServiceIdempotencyRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Health_GetAndHead_AreExemptFromRateLimiting()
    {
        using var rateLimitedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BusinessRateLimiting:OtherAnonymous:PermitLimit", "1");
            builder.UseSetting("BusinessRateLimiting:OtherAnonymous:WindowSeconds", "60");
        });
        using var client = rateLimitedFactory.CreateClient();

        var responses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            responses.Add(await client.GetAsync("/api/health"));
            responses.Add(await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, "/api/health")));
        }

        responses.Should().OnlyContain(response =>
            response.StatusCode != HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task InvalidHmac_CannotRotateHostHeaderToBypassPeerLimit()
    {
        using var rateLimitedFactory = CreateRateLimitedFactory();
        using var client = rateLimitedFactory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = await CreateAppointmentRequestAsync(
                client,
                $"step17-host-rotation-{attempt}",
                $"corr-step17-host-rotation-{attempt}",
                WrongSecret);
            request.Headers.Host = $"rotated-{attempt}.example.invalid";
            using var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(
                attempt < 3
                    ? HttpStatusCode.Unauthorized
                    : HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task InvalidHmac_CannotForgeForwardedForToBypassSocketPeerLimit()
    {
        using var rateLimitedFactory = CreateRateLimitedFactory(
            invalidWindowSeconds: 60);
        using var client = rateLimitedFactory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = await CreateAppointmentRequestAsync(
                client,
                $"step17-forwarded-for-{attempt}",
                $"corr-step17-forwarded-for-{attempt}",
                WrongSecret);
            request.Headers.TryAddWithoutValidation(
                "X-Forwarded-For",
                $"10.0.0.{attempt + 1}");
            using var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(
                attempt < 3
                    ? HttpStatusCode.Unauthorized
                    : HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task BusinessLogin_DuplicateEmailPropertiesUseFailClosedAccountPartition()
    {
        using var rateLimitedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "BusinessRateLimiting:BusinessAuth:PermitLimit",
                "3");
            builder.UseSetting(
                "BusinessRateLimiting:BusinessAuth:WindowSeconds",
                "60");
            builder.UseSetting(
                "BusinessRateLimiting:BusinessAuthAggregate:PermitLimit",
                "20");
            builder.UseSetting(
                "BusinessRateLimiting:BusinessAuthAggregate:WindowSeconds",
                "60");
        });
        using var client = rateLimitedFactory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/business/auth/login")
            {
                Content = new StringContent(
                    $$"""{"email":"rotated-{{attempt}}@example.invalid","EMAIL":"same@example.invalid","password":"Invalid1"}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
            using var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(
                attempt < 3
                    ? HttpStatusCode.Unauthorized
                    : HttpStatusCode.TooManyRequests);
        }
    }

    private WebApplicationFactory<Program> CreateRateLimitedFactory(
        bool useTransientIdempotencyStore = false,
        int invalidWindowSeconds = 1)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BusinessRateLimiting:QueueLimit", "0");
            builder.UseSetting("BusinessRateLimiting:ValidInternal:PermitLimit", "5");
            builder.UseSetting("BusinessRateLimiting:ValidInternal:WindowSeconds", "60");
            builder.UseSetting("BusinessRateLimiting:InvalidInternal:PermitLimit", "3");
            builder.UseSetting(
                "BusinessRateLimiting:InvalidInternal:WindowSeconds",
                invalidWindowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("BusinessRateLimiting:MissingPartitionKey", "missing");

            if (!useTransientIdempotencyStore)
            {
                return;
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInternalIdempotencyStore>();
                services.RemoveAll<IAppointmentValidationService>();
                services.AddSingleton<TransientUnavailableIdempotencyStore>();
                services.AddSingleton<IInternalIdempotencyStore>(provider =>
                    provider.GetRequiredService<TransientUnavailableIdempotencyStore>());
                services.AddSingleton<CountingAppointmentValidationService>();
                services.AddSingleton<IAppointmentValidationService>(provider =>
                    provider.GetRequiredService<CountingAppointmentValidationService>());
            });
        });
    }

    private static Task<HttpRequestMessage> CreateAppointmentRequestAsync(
        HttpClient client,
        string idempotencyKey,
        string correlationId,
        string secret)
    {
        return InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/validate",
            new
            {
                ContractVersion = BusinessCatalogContract.Version,
                BranchId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                OfferingId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                RequestedSlotStartUtc =
                    new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero),
                Currency = "ILS",
                PrivateMarker = RequestMarker
            },
            idempotencyKey,
            correlationId,
            secret: secret);
    }

    private static async Task AssertRateLimitProblemAsync(
        HttpResponseMessage response,
        string correlationId)
    {
        await AssertMachineProblemAsync(
            response,
            HttpStatusCode.TooManyRequests,
            "rate_limit_exceeded",
            "Too many requests.",
            "The request rate limit was exceeded. Retry after the indicated delay.");
        var retryAfter = response.Headers.RetryAfter?.Delta;
        retryAfter.Should().NotBeNull();
        retryAfter!.Value.Should().BeGreaterThan(TimeSpan.Zero)
            .And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
        response.Headers.Should().NotContain(header =>
            header.Key.StartsWith("RateLimit", StringComparison.OrdinalIgnoreCase) ||
            header.Key.StartsWith("X-RateLimit", StringComparison.OrdinalIgnoreCase));
        AssertTransportHeaders(response, correlationId);
    }

    private static async Task AssertMachineProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        string title,
        string detail)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, payload);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        response.Content.Headers.ContentLanguage.Should().BeEmpty();
        response.Headers.Vary.Should().NotContain(value =>
            value.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        root.GetProperty("type").GetString().Should()
            .Be($"https://api.ghseeli.example/errors/{code}");
        root.GetProperty("title").GetString().Should().Be(title);
        root.GetProperty("status").GetInt32().Should().Be((int)status);
        root.GetProperty("detail").GetString().Should().Be(detail);
        root.GetProperty("code").GetString().Should().Be(code);
        root.TryGetProperty("language", out _).Should().BeFalse();
        payload.Should().NotContain(RequestMarker);
        payload.Should().NotContain(WrongSecret);
        payload.Should().NotContain(CatalogApiFactory.InternalServiceActiveSecret);
        Step15BusinessHttpTestSupport.AssertRedacted(payload);
    }

    private static void AssertTransportHeaders(
        HttpResponseMessage response,
        string correlationId)
    {
        response.Headers.GetValues(InternalServiceWireConstants.CorrelationIdHeaderName)
            .Should().ContainSingle(correlationId);
        Step15BusinessHttpTestSupport.AssertSecurityHeaders(response);
        response.Headers.Should().NotContain(header =>
            header.Key == "Set-Cookie" ||
            header.Key == "ETag" ||
            header.Key == "Last-Modified" ||
            header.Key.StartsWith("Access-Control-Allow-", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TransientUnavailableIdempotencyStore : IInternalIdempotencyStore
    {
        private int _claimCount;
        private int _completionCount;

        public int ClaimCount => _claimCount;
        public int CompletionCount => _completionCount;

        public Task<InternalIdempotencyClaimResult> ClaimAsync(
            string serviceId,
            string operation,
            string idempotencyKey,
            string requestHash,
            DateTime nowUtc,
            DateTime expiresAtUtc,
            CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _claimCount);
            if (attempt <= 2)
            {
                return Task.FromResult(InternalIdempotencyClaimResult.InProgress(
                    new InternalServiceIdempotencyRecord
                    {
                        Id = Guid.NewGuid(),
                        ServiceId = serviceId,
                        Operation = operation,
                        IdempotencyKey = idempotencyKey,
                        RequestHash = requestHash,
                        State = InternalServiceIdempotencyState.InProgress,
                        CreatedAtUtc = nowUtc,
                        UpdatedAtUtc = nowUtc,
                        ExpiresAtUtc = expiresAtUtc
                    }));
            }

            return Task.FromResult(InternalIdempotencyClaimResult.Acquired(Guid.NewGuid()));
        }

        public Task CompleteAsync(
            Guid recordId,
            int statusCode,
            string contentType,
            string responseBody,
            DateTime completedAtUtc,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _completionCount);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid recordId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<InternalServiceIdempotencyRecord?> WaitForCompletionAsync(
            string serviceId,
            string operation,
            string idempotencyKey,
            TimeSpan timeout,
            TimeSpan pollInterval,
            CancellationToken cancellationToken) =>
            Task.FromResult<InternalServiceIdempotencyRecord?>(null);
    }

    private sealed class CountingAppointmentValidationService : IAppointmentValidationService
    {
        private int _invocationCount;
        public int InvocationCount => _invocationCount;

        public Task<ValidateAppointmentResponse> ValidateAsync(ValidateAppointmentRequest request)
        {
            Interlocked.Increment(ref _invocationCount);
            return Task.FromResult(new ValidateAppointmentResponse
            {
                ContractVersion = BusinessCatalogContract.Version,
                Valid = true,
                CatalogVersion = 1,
                Currency = request.Currency,
                BranchId = request.BranchId,
                OfferingId = request.OfferingId,
                TotalPrice = 100m,
                Availability = new AppointmentAvailabilityFacts
                {
                    IsAvailable = true,
                    RequestedSlotStartUtc = request.RequestedSlotStartUtc.UtcDateTime,
                    RequestedSlotEndUtc =
                        request.RequestedSlotStartUtc.UtcDateTime.AddMinutes(30)
                }
            });
        }
    }
}
