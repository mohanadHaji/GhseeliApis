using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.Services.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GhseeliApis.Tests.Services;

/// <summary>
/// Defines the Customer typed Business API client retry, signing, timeout, and failure-mapping behavior.
/// </summary>
public class BusinessApiClientTests
{
    [Fact]
    public async Task GetCatalogSnapshotAsync_RetriesRetryableFailuresWithFreshNoncesAndStableCorrelation()
    {
        var handler = new RecordingHandler(
        [
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Headers =
                {
                    RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero)
                }
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new CatalogSnapshotResponse
                    {
                        ContractVersion = "v1",
                        CatalogVersion = 7,
                        Company = new CatalogSnapshotCompany
                        {
                            Id = Guid.NewGuid(),
                            NameAr = "شركة"
                        }
                    },
                    BusinessCatalogContract.CreateJsonSerializerOptions()),
                    Encoding.UTF8,
                    "application/json")
            }
        ]);

        var client = CreateClient(handler, "corr-step6-client");

        var response = await client.GetCatalogSnapshotAsync(Guid.NewGuid());

        response.CatalogVersion.Should().Be(7);
        handler.Requests.Should().HaveCount(2);
        handler.Requests.Select(request => request.Headers.GetValues("X-Correlation-Id").Single())
            .Should()
            .OnlyContain(value => value == "corr-step6-client");
        handler.Requests.Select(request => request.Headers.GetValues("X-Ghseeli-Nonce").Single())
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ValidateAppointmentAsync_WhenServerReturnsConflict_ThrowsTypedConflictAndDoesNotRetry()
    {
        var handler = new RecordingHandler(
        [
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    "{\"code\":\"idempotency_conflict\",\"detail\":\"conflict\"}",
                    Encoding.UTF8,
                    "application/problem+json")
            }
        ]);

        var client = CreateClient(handler, "corr-step6-conflict");

        var action = () => client.ValidateAppointmentAsync(
            new ValidateAppointmentRequest
            {
                BranchId = Guid.NewGuid(),
                OfferingId = Guid.NewGuid(),
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(1),
                Currency = "ILS"
            },
            "idem-step6");

        await action.Should().ThrowAsync<BusinessApiConflictException>();
        handler.Requests.Should().HaveCount(1);
        handler.Requests.Single().Headers.GetValues("Idempotency-Key").Single()
            .Should()
            .Be("idem-step6");
    }

    [Fact]
    public async Task CreateReservationAsync_WhenServerReturnsConflict_PreservesStructuredCode()
    {
        var handler = new RecordingHandler(
        [
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    "{\"code\":\"PRICE_CHANGED\",\"message\":\"changed\"}",
                    Encoding.UTF8,
                    "application/json")
            }
        ]);
        var client = CreateClient(handler, "corr-step12-conflict");

        var action = () => client.CreateReservationAsync(
            new CreateReservationRequest
            {
                BookingReference = Guid.NewGuid(),
                OrderGuid = Guid.NewGuid(),
                BranchId = Guid.NewGuid(),
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(1),
                Currency = "ILS",
                CancellationPolicyAcknowledged = true
            },
            "booking-step12");

        var exception = await action.Should().ThrowAsync<BusinessApiConflictException>();
        exception.Which.Code.Should().Be(ReservationErrorCodes.PriceChanged);
        exception.Which.CorrelationId.Should().Be("corr-step12-conflict");
    }

    [Fact]
    public async Task GetCatalogSnapshotAsync_WhenSuccessBodyIsEmpty_ThrowsTypedContractException()
    {
        var handler = new RecordingHandler(
        [
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            }
        ]);

        var client = CreateClient(handler);

        var action = () => client.GetCatalogSnapshotAsync(Guid.NewGuid());

        await action.Should().ThrowAsync<BusinessApiContractException>();
    }

    [Fact]
    public async Task ValidateAppointmentAsync_RetriesTransientFailuresWithStableIdempotencyAndFreshNonces()
    {
        var handler = new RecordingHandler(
        [
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new ValidateAppointmentResponse
                    {
                        ContractVersion = BusinessCatalogContract.Version,
                        Valid = true,
                        CatalogVersion = 11,
                        Currency = "ILS",
                        BranchId = Guid.NewGuid(),
                        OfferingId = Guid.NewGuid(),
                        TotalPrice = 70.02m
                    }, BusinessCatalogContract.CreateJsonSerializerOptions()),
                    Encoding.UTF8,
                    "application/json")
            }
        ]);

        var client = CreateClient(handler, "corr-step6-validate-retry");

        var response = await client.ValidateAppointmentAsync(
            new ValidateAppointmentRequest
            {
                BranchId = Guid.NewGuid(),
                OfferingId = Guid.NewGuid(),
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(1),
                Currency = "ILS"
            },
            "idem-step6-retry");

        response.Valid.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
        handler.Requests.Select(request => request.Headers.GetValues(
                InternalServiceWireConstants.IdempotencyKeyHeaderName).Single())
            .Should()
            .OnlyContain(value => value == "idem-step6-retry");
        handler.Requests.Select(request => request.Headers.GetValues(
                InternalServiceWireConstants.NonceHeaderName).Single())
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ValidateAppointmentAsync_SignsIdempotencyKeyIntoCanonicalRequest()
    {
        var handler = new RecordingHandler(
        [
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new ValidateAppointmentResponse
                    {
                        ContractVersion = BusinessCatalogContract.Version,
                        Valid = true,
                        CatalogVersion = 5,
                        Currency = "ILS",
                        BranchId = Guid.NewGuid(),
                        OfferingId = Guid.NewGuid(),
                        TotalPrice = 70.02m
                    }, BusinessCatalogContract.CreateJsonSerializerOptions()),
                    Encoding.UTF8,
                    "application/json")
            }
        ]);

        var client = CreateClient(handler, "corr-step6-signing-idempotency");
        const string idempotencyKey = "idem-step6-signature";

        await client.ValidateAppointmentAsync(
            new ValidateAppointmentRequest
            {
                BranchId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                OfferingId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                RequestedSlotStartUtc = new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero),
                Currency = "ILS"
            },
            idempotencyKey);

        var request = handler.Requests.Single();
        var timestamp = request.Headers.GetValues(InternalServiceWireConstants.TimestampHeaderName).Single();
        var nonce = request.Headers.GetValues(InternalServiceWireConstants.NonceHeaderName).Single();
        var signature = request.Headers.GetValues(InternalServiceWireConstants.SignatureHeaderName).Single();
        var bodyBytes = await request.Content!.ReadAsByteArrayAsync();
        var bodyHash = InternalServiceCanonicalRequest.ComputeSha256Hex(bodyBytes);

        var canonicalWithIdempotency = string.Join('\n',
            InternalServiceWireConstants.SignatureVersion,
            "customer-api-tests",
            HttpMethod.Post.Method,
            "/api/v1/internal/appointments/validate",
            timestamp,
            nonce,
            idempotencyKey,
            bodyHash);
        var canonicalWithoutIdempotency = string.Join('\n',
            InternalServiceWireConstants.SignatureVersion,
            "customer-api-tests",
            HttpMethod.Post.Method,
            "/api/v1/internal/appointments/validate",
            timestamp,
            nonce,
            string.Empty,
            bodyHash);

        signature.Should().Be(ComputeSignature(
            "CustomerStep6ActiveSecret_Minimum32Chars",
            canonicalWithIdempotency));
        signature.Should().NotBe(ComputeSignature(
            "CustomerStep6ActiveSecret_Minimum32Chars",
            canonicalWithoutIdempotency));
    }

    [Fact]
    public async Task GetCatalogSnapshotAsync_WhenServerReturnsBadRequest_ThrowsContractExceptionWithoutRetry()
    {
        var handler = new RecordingHandler(
        [
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"detail\":\"bad request\"}",
                    Encoding.UTF8,
                    "application/problem+json")
            }
        ]);

        var client = CreateClient(handler, "corr-step6-bad-request");

        var action = () => client.GetCatalogSnapshotAsync(Guid.NewGuid());

        await action.Should().ThrowAsync<BusinessApiContractException>();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetCatalogSnapshotAsync_WhenClientConfigurationIsInvalid_ThrowsTypedConfigurationWithoutRetry()
    {
        var handler = new RecordingHandler(
        [
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new CatalogSnapshotResponse
                    {
                        ContractVersion = BusinessCatalogContract.Version,
                        Company = new CatalogSnapshotCompany
                        {
                            Id = Guid.NewGuid(),
                            NameAr = "شركة"
                        }
                    }, BusinessCatalogContract.CreateJsonSerializerOptions()),
                    Encoding.UTF8,
                    "application/json")
            }
        ]);

        var client = CreateClient(
            handler,
            "corr-step9-config",
            configureOptions: options => options.BaseUrl = string.Empty);

        var action = () => client.GetCatalogSnapshotAsync(Guid.NewGuid());

        await action.Should().ThrowAsync<BusinessApiConfigurationException>()
            .Where(exception => exception.CorrelationId == "corr-step9-config");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCatalogSnapshotAsync_WhenAttemptTimesOut_ThrowsTypedTimeoutException()
    {
        var handler = new TimeoutHandler(TimeSpan.FromMilliseconds(250));
        var client = CreateClient(handler, environmentName: "Development", timeoutSeconds: 0.05, maxRetryAttempts: 0);

        var action = () => client.GetCatalogSnapshotAsync(Guid.NewGuid());

        await action.Should().ThrowAsync<BusinessApiTimeoutException>();
    }

    [Fact]
    public async Task ValidateAppointmentAsync_DeserializesBusinessValidateResponseContract()
    {
        var expectedResponse = new ValidateAppointmentResponse
        {
            ContractVersion = BusinessCatalogContract.Version,
            Valid = true,
            CatalogVersion = 9,
            Currency = "ILS",
            BranchId = Guid.NewGuid(),
            OfferingId = Guid.NewGuid(),
            BaseSubtotal = 50.00m,
            AddonSubtotal = 20.02m,
            TotalPrice = 70.02m,
            Availability = new AppointmentAvailabilityFacts
            {
                IsAvailable = true,
                RequestedSlotStartUtc = new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc),
                RequestedSlotEndUtc = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc)
            }
        };
        var handler = new RecordingHandler(
        [
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(expectedResponse, BusinessCatalogContract.CreateJsonSerializerOptions()),
                    Encoding.UTF8,
                    "application/json")
            }
        ]);

        var client = CreateClient(handler, "corr-step6-real-contract");

        var response = await client.ValidateAppointmentAsync(
            new ValidateAppointmentRequest
            {
                BranchId = expectedResponse.BranchId,
                OfferingId = expectedResponse.OfferingId,
                RequestedSlotStartUtc = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.FromHours(3)),
                Currency = "ILS"
            },
            "idem-step6-real-contract");

        response.TotalPrice.Should().Be(expectedResponse.TotalPrice);
        response.Availability.RequestedSlotStartUtc.Should().Be(expectedResponse.Availability.RequestedSlotStartUtc);
    }

    private static IBusinessApiClient CreateClient(
        HttpMessageHandler innerHandler,
        string? correlationId = null,
        string environmentName = "Production",
        double timeoutSeconds = 1,
        int maxRetryAttempts = 1,
        Action<BusinessApiClientOptions>? configureOptions = null)
    {
        var optionValues = new BusinessApiClientOptions
        {
            BaseUrl = "https://business.example.test",
            ServiceId = "customer-api-tests",
            ActiveSecret = "CustomerStep6ActiveSecret_Minimum32Chars",
            TimeoutSeconds = timeoutSeconds,
            MaxRetryAttempts = maxRetryAttempts,
            MaxRetryAfterSeconds = 1
        };
        configureOptions?.Invoke(optionValues);
        var options = Options.Create(optionValues);

        var context = new DefaultHttpContext();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            context.Request.Headers["X-Correlation-Id"] = correlationId;
        }

        var accessor = new HttpContextAccessor
        {
            HttpContext = context
        };

        var environment = new TestHostEnvironment
        {
            EnvironmentName = environmentName
        };

        var correlationHandler = new CorrelationIdPropagationHandler(accessor);
        var signingHandler = new HmacSigningDelegatingHandler(options);
        signingHandler.InnerHandler = innerHandler;
        correlationHandler.InnerHandler = signingHandler;
        var resilienceHandler = new BusinessApiResilienceDelegatingHandler(options);
        resilienceHandler.InnerHandler = correlationHandler;

        var httpClient = new HttpClient(resilienceHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        if (Uri.TryCreate(options.Value.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            httpClient.BaseAddress = baseUri;
        }

        return new BusinessApiClient(httpClient, options, environment, accessor);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public RecordingHandler(IEnumerable<HttpResponseMessage> responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(_responses.Dequeue());
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);

            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var content = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    request.Content.Headers.ContentType?.MediaType ?? "application/json");
            }

            return clone;
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;

        public TimeoutHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new CatalogSnapshotResponse
                    {
                        ContractVersion = BusinessCatalogContract.Version,
                        Company = new CatalogSnapshotCompany
                        {
                            Id = Guid.NewGuid(),
                            NameAr = "شركة"
                        }
                    }, BusinessCatalogContract.CreateJsonSerializerOptions()),
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GhseeliApis.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static string ComputeSignature(string secret, string canonicalRequest)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalRequest)))
            .ToLowerInvariant();
    }
}
