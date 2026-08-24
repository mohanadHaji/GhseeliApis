using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.DTOs.Payment;
using GhseeliApis.Models;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Payments;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Final cross-cutting Customer proofs for the frozen Step 15 invariants.
/// </summary>
public sealed class Step15FinalInvariantCustomerTests
{
    private const string JwtSecret = "CheckoutDraftApiTestsSecret_Minimum32Chars";

    [Fact]
    [Trait("ScenarioId", "STEP15-TRANSPORT-QUERY-ENCODING-129")]
    public async Task Encoded_query_names_values_and_duplicates_have_runtime_semantics()
    {
        var device = CatalogTestSupport.CreateDevice(
            Step15CustomerFixture.DeviceToken,
            DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory([device]);
        using var client = factory.CreateApiClient();

        using var encoded = await client.GetAsync(
            "/api/v1/configuration?lang%75age=%68%65");
        using var duplicate = await client.GetAsync(
            "/api/v1/configuration?lang%75age=ar&language=%68%65");

        encoded.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        encoded.Content.Headers.ContentLanguage.Should().ContainSingle("he");
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("language_invalid");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-SWAGGER-DETERMINISTIC-151")]
    public async Task Repeated_OpenAPI_generation_is_byte_stable_with_unique_operations_and_resolved_refs()
    {
        await using var factory = new Step15SwaggerCustomerFactory();
        using var client = factory.CreateApiClient();

        var first = await client.GetByteArrayAsync("/swagger/v1/swagger.json");
        var second = await client.GetByteArrayAsync("/swagger/v1/swagger.json");

        second.Should().Equal(first, "repeated generation on one immutable host must be byte deterministic");
        using var document = JsonDocument.Parse(first);
        var operationIds = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(operation => IsHttpMethod(operation.Name))
            .Select(operation => operation.Value.GetProperty("operationId").GetString())
            .ToArray();
        operationIds.Should().NotContainNulls().And.OnlyHaveUniqueItems();
        AssertAllLocalReferencesResolve(document.RootElement);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-CROSS-HOST-CORRELATION-160")]
    public async Task Customer_to_Business_transport_preserves_safe_identity_and_refreshes_HMAC_nonce()
    {
        var terminal = new CapturingBusinessHandler();
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        accessor.HttpContext.Request.Headers["X-Correlation-Id"] = "step15-cross-host-correlation";
        var options = Options.Create(new BusinessApiClientOptions
        {
            BaseUrl = "https://business.example",
            ServiceId = "customer-step15",
            ActiveSecret = "Step15CrossHostSecret_Minimum32Characters",
            RequireHttps = true
        });
        var signer = new HmacSigningDelegatingHandler(options) { InnerHandler = terminal };
        var client = new BusinessApiClient(
            new HttpClient(signer),
            options,
            new TestEnvironment(),
            accessor);
        var request = new ValidateAppointmentRequest
        {
            BranchId = Guid.NewGuid(),
            OfferingId = Guid.NewGuid(),
            RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(1),
            Currency = "ILS"
        };

        await client.ValidateAppointmentAsync(request, "step15-stable-idempotency");
        await client.ValidateAppointmentAsync(request, "step15-stable-idempotency");

        terminal.Requests.Should().HaveCount(2);
        terminal.Requests.Select(x => x.CorrelationId).Should()
            .OnlyContain(x => x == "step15-cross-host-correlation");
        terminal.Requests.Select(x => x.IdempotencyKey).Should()
            .OnlyContain(x => x == "step15-stable-idempotency");
        terminal.Requests.Select(x => x.Nonce).Should().OnlyHaveUniqueItems()
            .And.OnlyContain(x => x.Length == 43 &&
                x.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        terminal.Requests.Should().OnlyContain(x =>
            x.Signature.Length == 64 && x.Signature.All(Uri.IsHexDigit));
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-UPSTREAM-MALFORMED-161")]
    public async Task Malformed_upstream_success_maps_to_safe_stable_HTTP_problem_without_raw_body()
    {
        const string rawUpstream = "provider-secret-body<not-json>";
        var upstream = new MalformedBusinessHandler(rawUpstream);
        var options = Options.Create(new BusinessApiClientOptions
        {
            BaseUrl = "https://business.example",
            ServiceId = "customer-step15",
            ActiveSecret = "Step15CrossHostSecret_Minimum32Characters",
            RequireHttps = true
        });
        var typedClient = new BusinessApiClient(
            new HttpClient(upstream),
            options,
            new TestEnvironment(),
            new HttpContextAccessor());
        var upstreamAction = () => typedClient.GetCatalogSnapshotAsync(Guid.NewGuid());
        var contractFailure = await upstreamAction.Should()
            .ThrowAsync<BusinessApiContractException>();
        contractFailure.Which.Message.Should().NotContain(rawUpstream);

        var service = new MockPaymentService((_, _, _, _, _) =>
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.ProviderUnavailable,
                contractFailure.Which.Message));
        var device = CatalogTestSupport.CreateDevice(
            Step15CustomerFixture.DeviceToken,
            DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(
            [device],
            services =>
            {
                services.RemoveAll<ICustomerPaymentService>();
                services.AddSingleton<ICustomerPaymentService>(service);
            });
        using var response = await SendPaymentAsync(
            factory.CreateApiClient(), "malformed-upstream", Jwt(Guid.NewGuid()));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString()
            .Should().Be(CustomerPaymentErrorCodes.ProviderUnavailable);
        body.Should().NotContain(rawUpstream).And.NotContain("Malformed Business response");
        service.CallCount.Should().Be(1);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-DB-FAILURE-162")]
    public async Task Database_failpoint_maps_retryably_and_has_no_committed_side_effect()
    {
        const string databaseDetail =
            "Server=private-db;Password=raw-password; controlled save failure";
        var service = new MockPaymentService((_, _, _, _, _) =>
            throw new DbUpdateException(databaseDetail));
        var device = CatalogTestSupport.CreateDevice(
            Step15CustomerFixture.DeviceToken,
            DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(
            [device],
            services =>
            {
                services.RemoveAll<ICustomerPaymentService>();
                services.AddSingleton<ICustomerPaymentService>(service);
            });

        using var response = await SendPaymentAsync(
            factory.CreateApiClient(), "db-failpoint", Jwt(Guid.NewGuid()));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().Should().Be("service_unavailable");
        body.Should().NotContain(databaseDetail)
            .And.NotContain("private-db")
            .And.NotContain("raw-password");
        service.CommittedCount.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-CONCURRENCY-163")]
    public async Task Parallel_same_key_Customer_mutations_have_one_winner_through_HTTP_middleware()
    {
        var service = new OneWinnerPaymentService();
        var device = CatalogTestSupport.CreateDevice(
            Step15CustomerFixture.DeviceToken,
            DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(
            [device],
            services =>
            {
                services.RemoveAll<ICustomerPaymentService>();
                services.AddSingleton<ICustomerPaymentService>(service);
            });
        using var client = factory.CreateApiClient();
        var jwt = Jwt(Guid.NewGuid());

        var responses = await Task.WhenAll(
            SendPaymentAsync(client, "same-http-key", jwt),
            SendPaymentAsync(client, "same-http-key", jwt));
        var bodies = await Task.WhenAll(responses.Select(x => x.Content.ReadAsStringAsync()));

        responses.Should().OnlyContain(x => x.StatusCode == HttpStatusCode.OK);
        bodies[0].Should().Be(bodies[1]);
        service.WinnerCount.Should().Be(1);
        service.CallCount.Should().Be(2);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-PROBLEM-REDACTION-032")]
    public async Task Captured_logs_redact_representative_JWT_device_HMAC_Stripe_and_provider_failures()
    {
        var logs = new CapturingLoggerProvider();
        var service = new MockPaymentService((_, _, _, _, _) =>
            throw new CustomerPaymentException(
                503,
                CustomerPaymentErrorCodes.ProviderUnavailable,
                "provider-raw-secret"));
        var device = CatalogTestSupport.CreateDevice(
            Step15CustomerFixture.DeviceToken,
            DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(
            [device],
            services =>
            {
                services.AddSingleton<ILoggerProvider>(logs);
                services.RemoveAll<ICustomerPaymentService>();
                services.AddSingleton<ICustomerPaymentService>(service);
            });
        using var client = factory.CreateApiClient();

        using (var jwtFailure = new HttpRequestMessage(HttpMethod.Get, "/api/v1/payments/" + Guid.NewGuid()))
        {
            jwtFailure.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", "jwt-sensitive-value");
            jwtFailure.Headers.TryAddWithoutValidation("X-Device-Token", Step15CustomerFixture.DeviceToken);
            using var _ = await client.SendAsync(jwtFailure);
        }
        using (var deviceFailure = new HttpRequestMessage(HttpMethod.Get, "/api/v1/configuration"))
        {
            deviceFailure.Headers.TryAddWithoutValidation("X-Device-Token", "device-sensitive-value");
            using var _ = await client.SendAsync(deviceFailure);
        }
        using (var hmacFailure = new HttpRequestMessage(
                   HttpMethod.Post, "/api/v1/internal/bookings/status"))
        {
            hmacFailure.Headers.TryAddWithoutValidation("X-Ghseeli-Service-Id", "hmac-sensitive-service");
            hmacFailure.Headers.TryAddWithoutValidation("X-Ghseeli-Signature", "hmac-sensitive-signature");
            hmacFailure.Content = new StringContent("{\"providerSecret\":\"hmac-sensitive-body\"}", Encoding.UTF8, "application/json");
            using var _ = await client.SendAsync(hmacFailure);
        }
        using (var stripeFailure = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook"))
        {
            stripeFailure.Headers.TryAddWithoutValidation("Stripe-Signature", "stripe-sensitive-signature");
            stripeFailure.Content = new StringContent("stripe-sensitive-body", Encoding.UTF8, "application/json");
            using var _ = await client.SendAsync(stripeFailure);
        }
        using var providerFailure = await SendPaymentAsync(
            client, "provider-sensitive-idempotency", Jwt(Guid.NewGuid()));

        var captured = string.Join("\n", logs.Messages);
        captured.Should().NotContainAny(
            "jwt-sensitive-value",
            Step15CustomerFixture.DeviceToken,
            "device-sensitive-value",
            "hmac-sensitive-signature",
            "hmac-sensitive-body",
            "stripe-sensitive-signature",
            "stripe-sensitive-body",
            "provider-raw-secret",
            "provider-sensitive-idempotency");
        logs.Messages.Should().NotBeEmpty("the assertion must inspect actual emitted framework/application logs");
    }

    private static Step15CustomerApiFactory CreateFactory(
        IReadOnlyCollection<CustomerDevice> devices,
        Action<IServiceCollection>? configure = null)
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(
            Guid.Parse("15151515-1515-1515-1515-151515151515"),
            version: 15,
            companyNameAr: "شركة الاختبار",
            companyNameHe: null);
        return new Step15CustomerApiFactory(snapshot, devices, configure);
    }

    private static async Task<HttpResponseMessage> SendPaymentAsync(
        HttpClient client,
        string idempotencyKey,
        string jwt)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/intents")
        {
            Content = new StringContent(
                $$"""{"bookingId":"{{Guid.Parse("11111111-1111-1111-1111-111111111111")}}","method":"Card"}""",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        request.Headers.TryAddWithoutValidation("X-Device-Token", Step15CustomerFixture.DeviceToken);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static string Jwt(Guid userId)
    {
        var token = new JwtSecurityToken(
            issuer: "GhseeliApis.CheckoutDraftTests",
            audience: "GhseeliApis.CheckoutDraftClients",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "User")
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static bool IsHttpMethod(string name) =>
        name is "get" or "put" or "post" or "delete" or "patch" or "options" or "head" or "trace";

    private static void AssertAllLocalReferencesResolve(JsonElement root)
    {
        foreach (var reference in EnumerateReferences(root))
        {
            reference.Should().StartWith("#/");
            var current = root;
            foreach (var rawSegment in reference[2..].Split('/'))
            {
                var segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
                current.TryGetProperty(segment, out current).Should().BeTrue(
                    $"OpenAPI reference '{reference}' must resolve");
            }
        }
    }

    private static IEnumerable<string> EnumerateReferences(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("$ref"))
                {
                    yield return property.Value.GetString()!;
                }
                foreach (var nested in EnumerateReferences(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var nested in EnumerateReferences(item))
            {
                yield return nested;
            }
        }
    }

    private sealed class CapturingBusinessHandler : HttpMessageHandler
    {
        public ConcurrentBag<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Headers.GetValues("X-Correlation-Id").Single(),
                request.Headers.GetValues("Idempotency-Key").Single(),
                request.Headers.GetValues("X-Ghseeli-Nonce").Single(),
                request.Headers.GetValues("X-Ghseeli-Signature").Single()));
            var payload = new ValidateAppointmentResponse
            {
                ContractVersion = BusinessCatalogContract.Version,
                Valid = true,
                CatalogVersion = 1,
                Currency = "ILS",
                BranchId = Guid.NewGuid(),
                OfferingId = Guid.NewGuid(),
                TotalPrice = 1m
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    payload,
                    options: BusinessCatalogContract.CreateJsonSerializerOptions())
            });
        }
    }

    private sealed record CapturedRequest(
        string CorrelationId,
        string IdempotencyKey,
        string Nonce,
        string Signature);

    private sealed class MalformedBusinessHandler(string rawBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(rawBody, Encoding.UTF8, "application/json")
            });
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Step15";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class MockPaymentService : ICustomerPaymentService
    {
        private readonly Func<CreateCustomerPaymentIntentRequest, string, Guid, Guid, CancellationToken, CustomerPaymentResponse> _create;
        private int _calls;
        private int _committed;

        public MockPaymentService(
            Func<CreateCustomerPaymentIntentRequest, string, Guid, Guid, CancellationToken, CustomerPaymentResponse> create) =>
            _create = create;

        public int CallCount => Volatile.Read(ref _calls);
        public int CommittedCount => Volatile.Read(ref _committed);

        public Task<CustomerPaymentResponse> CreateAsync(
            CreateCustomerPaymentIntentRequest request,
            string idempotencyKey,
            Guid userId,
            Guid deviceId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var result = _create(request, idempotencyKey, userId, deviceId, cancellationToken);
            Interlocked.Increment(ref _committed);
            return Task.FromResult(result);
        }

        public Task<CustomerPaymentResponse?> GetAsync(
            Guid paymentId,
            Guid userId,
            Guid deviceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CustomerPaymentResponse?>(null);
    }

    private sealed class OneWinnerPaymentService : ICustomerPaymentService
    {
        private readonly ConcurrentDictionary<string, Lazy<Task<CustomerPaymentResponse>>> _results =
            new(StringComparer.Ordinal);
        private int _calls;
        private int _winners;

        public int CallCount => Volatile.Read(ref _calls);
        public int WinnerCount => Volatile.Read(ref _winners);

        public Task<CustomerPaymentResponse> CreateAsync(
            CreateCustomerPaymentIntentRequest request,
            string idempotencyKey,
            Guid userId,
            Guid deviceId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return _results.GetOrAdd(
                idempotencyKey,
                _ => new Lazy<Task<CustomerPaymentResponse>>(
                    async () =>
                    {
                        Interlocked.Increment(ref _winners);
                        await Task.Delay(50, cancellationToken);
                        return new CustomerPaymentResponse
                        {
                            Id = Guid.Parse("16316316-3163-4163-8163-163163163163"),
                            BookingId = request.BookingId,
                            Amount = 25m,
                            Currency = "ILS",
                            Method = "Card",
                            Status = "Pending",
                            CreatedAtUtc = DateTimeOffset.UnixEpoch
                        };
                    },
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        public Task<CustomerPaymentResponse?> GetAsync(
            Guid paymentId,
            Guid userId,
            Guid deviceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CustomerPaymentResponse?>(null);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }
}
