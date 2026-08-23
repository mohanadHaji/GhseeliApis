using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Defines the step-6 internal HMAC authentication, replay protection, idempotency, and correlation behavior.
/// </summary>
public class InternalServiceSecurityIntegrationTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public InternalServiceSecurityIntegrationTests(CatalogApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CatalogSnapshot_WhenSignedRequestIsValid_ReturnsSnapshotAndEchoesCorrelationId()
    {
        _factory.ResetState();
        var ownerClient = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        _ = await ownerClient.PostAsJsonAsync("/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = "تنظيف",
                DisplayOrder = 0,
                IsActive = true
            });

        using var internalClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            internalClient,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            correlationId: "corr-step6-valid-snapshot");

        var response = await internalClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, content);
        response.Headers.GetValues(Ghseeli.IntegrationContracts.InternalHttp.InternalServiceWireConstants.CorrelationIdHeaderName).Single()
            .Should()
            .Be("corr-step6-valid-snapshot");

        using var document = JsonDocument.Parse(content);
        document.RootElement.GetProperty("company").GetProperty("id").GetGuid()
            .Should()
            .Be(_factory.CompanyId);
    }

    [Fact]
    public async Task CatalogSnapshot_WhenBusinessJwtIsUsedWithoutServiceHeaders_ReturnsUnauthorized()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        var response = await client.GetAsync(
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownInternalRoute_WhenRequestIsSigned_ReturnsNotFoundWithoutConsumingNonce()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        var nonce = Guid.NewGuid().ToString("N");
        using var unknownRequest = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            "/api/v1/internal/unknown-step13-route",
            nonce: nonce);

        var unknownResponse = await client.SendAsync(unknownRequest);

        unknownResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var protectedRequest = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            nonce: nonce);
        var protectedResponse = await client.SendAsync(protectedRequest);

        protectedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(InternalServiceWireConstants.ServiceIdHeaderName)]
    [InlineData(InternalServiceWireConstants.TimestampHeaderName)]
    [InlineData(InternalServiceWireConstants.NonceHeaderName)]
    [InlineData(InternalServiceWireConstants.SignatureHeaderName)]
    public async Task CatalogSnapshot_WhenRequiredHeaderIsMissing_ReturnsUnauthorized(
        string headerName)
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}");
        request.Headers.Remove(headerName);

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        content.Should().Contain(InternalServiceProblemCodes.MissingAuthenticationHeader);
    }

    [Fact]
    public async Task CatalogSnapshot_WhenTimestampIsMalformed_ReturnsUnauthorized()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}");
        request.Headers.Remove(InternalServiceWireConstants.TimestampHeaderName);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.TimestampHeaderName,
            "not-a-timestamp");

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        content.Should().Contain(InternalServiceProblemCodes.InvalidTimestamp);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(10)]
    public async Task CatalogSnapshot_WhenTimestampIsOutsideClockSkew_ReturnsUnauthorized(
        int minutesOffset)
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            timestamp: DateTimeOffset.UtcNow.AddMinutes(minutesOffset));

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        content.Should().Contain(InternalServiceProblemCodes.TimestampOutOfRange);
    }

    [Theory]
    [InlineData("unknown-service", CatalogApiFactory.InternalServiceActiveSecret, HttpStatusCode.Unauthorized, InternalServiceProblemCodes.InvalidServiceId)]
    [InlineData(CatalogApiFactory.InternalServiceId, "wrong-secret-minimum-32-characters___", HttpStatusCode.Unauthorized, InternalServiceProblemCodes.InvalidSignature)]
    public async Task CatalogSnapshot_WhenServiceIdentityOrSecretIsWrong_ReturnsExpectedProblem(
        string serviceId,
        string secret,
        HttpStatusCode expectedStatusCode,
        string expectedCode)
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            serviceId: serviceId,
            secret: secret);

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(expectedStatusCode);
        content.Should().Contain(expectedCode);
    }

    [Fact]
    public async Task CatalogSnapshot_WhenSignedRequestIsSentOverHttpWithoutExplicitDevelopmentOverride_ReturnsForbidden()
    {
        _factory.ResetState();
        using var client = _factory.CreateClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CatalogSnapshot_WhenDevelopmentHttpOverrideIsEnabled_AllowsSignedHttpRequest()
    {
        _factory.ResetState();
        using var overrideFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("InternalServiceAuthentication:AllowInsecureHttpInDevelopment", "true"));
        using var client = overrideFactory.CreateClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ValidateAppointment_WhenSameIdempotencyKeyIsReusedWithDifferentBodies_ReturnsConflict()
    {
        _factory.ResetState();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        const string idempotencyKey = "validate-step6-conflict";

        using var firstRequest = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/validate",
            new ValidateAppointmentRequest
            {
                BranchId = Guid.NewGuid(),
                OfferingId = Guid.NewGuid(),
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(1),
                Currency = "ILS"
            },
            idempotencyKey);

        using var secondRequest = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/validate",
            new ValidateAppointmentRequest
            {
                BranchId = Guid.NewGuid(),
                OfferingId = Guid.NewGuid(),
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(2),
                Currency = "ILS"
            },
            idempotencyKey);

        var firstResponse = await client.SendAsync(firstRequest);
        var secondResponse = await client.SendAsync(secondRequest);

        firstResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CatalogSnapshot_WhenNonceIsReusedSequentially_ReturnsUnauthorized()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        var nonce = Guid.NewGuid().ToString("N");
        using var firstRequest = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            nonce: nonce);
        using var secondRequest = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            nonce: nonce);

        var firstResponse = await client.SendAsync(firstRequest);
        var secondResponse = await client.SendAsync(secondRequest);
        var secondContent = await secondResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        secondContent.Should().Contain(InternalServiceProblemCodes.ReplayNonce);
    }

    [Fact]
    public async Task CatalogSnapshot_WhenSignedWithNextSecretDuringRotation_ReturnsSuccess()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            secret: CatalogApiFactory.InternalServiceNextSecret);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ValidateAppointment_WhenServiceIsAuthenticatedButNotAllowedForOperation_ReturnsForbidden()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/validate",
            new ValidateAppointmentRequest
            {
                BranchId = Guid.NewGuid(),
                OfferingId = Guid.NewGuid(),
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(1),
                Currency = "ILS"
            },
            idempotencyKey: "idem-snapshot-only",
            serviceId: CatalogApiFactory.SnapshotOnlyServiceId,
            secret: CatalogApiFactory.SnapshotOnlySecret);

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        content.Should().Contain(InternalServiceProblemCodes.ServiceForbidden);
    }

    [Fact]
    public async Task ValidateAppointment_WhenBodyIsTamperedAfterSigning_ReturnsUnauthorized()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/validate",
            new ValidateAppointmentRequest
            {
                BranchId = Guid.NewGuid(),
                OfferingId = Guid.NewGuid(),
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(1),
                Currency = "ILS"
            },
            idempotencyKey: "idem-body-tamper");
        request.Content = JsonContent.Create(new ValidateAppointmentRequest
        {
            BranchId = Guid.NewGuid(),
            OfferingId = Guid.NewGuid(),
            RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(2),
            Currency = "ILS"
        });

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        content.Should().Contain(InternalServiceProblemCodes.InvalidSignature);
    }

    [Fact]
    public async Task ValidateAppointment_WhenIdempotencyKeyIsTamperedAfterSigning_ReturnsUnauthorized()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/validate",
            new ValidateAppointmentRequest
            {
                BranchId = Guid.NewGuid(),
                OfferingId = Guid.NewGuid(),
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(1),
                Currency = "ILS"
            },
            idempotencyKey: "idem-key-before-tamper");
        request.Headers.Remove(InternalServiceWireConstants.IdempotencyKeyHeaderName);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.IdempotencyKeyHeaderName,
            "idem-key-after-tamper");

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        content.Should().Contain(InternalServiceProblemCodes.InvalidSignature);
    }

    [Fact]
    public async Task CatalogSnapshot_WhenQueryIsTamperedAfterSigning_ReturnsUnauthorized()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}");
        request.RequestUri = new Uri(
            client.BaseAddress!,
            $"/api/v1/internal/catalog/snapshot?companyId={Guid.NewGuid()}");

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        content.Should().Contain(InternalServiceProblemCodes.InvalidSignature);
    }

    [Fact]
    public async Task CatalogSnapshot_WhenQueryParametersAreOutOfOrder_StillUsesCanonicalOrdering()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?z=1&companyId={_factory.CompanyId}&a=2");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ValidateAppointment_WhenKnownContentLengthBodyExceedsConfiguredLimit_ReturnsPayloadTooLarge()
    {
        _factory.ResetState();
        using var oversizedFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("InternalServiceAuthentication:MaxRequestBodyBytes", "128"));
        using var client = oversizedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/validate",
            new
            {
                ContractVersion = BusinessCatalogContract.Version,
                BranchId = Guid.NewGuid(),
                OfferingId = Guid.NewGuid(),
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(1),
                Currency = "ILS",
                Padding = new string('x', 512)
            },
            idempotencyKey: "idem-known-length-oversized");

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        content.Should().Contain(InternalServiceProblemCodes.RequestBodyTooLarge);
    }

    [Fact]
    public async Task ValidateAppointment_WhenChunkedBodyExceedsConfiguredLimit_ReturnsPayloadTooLarge()
    {
        _factory.ResetState();
        using var oversizedFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("InternalServiceAuthentication:MaxRequestBodyBytes", "128"));
        using var client = oversizedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/validate",
            new
            {
                ContractVersion = BusinessCatalogContract.Version,
                BranchId = Guid.NewGuid(),
                OfferingId = Guid.NewGuid(),
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(1),
                Currency = "ILS",
                Padding = new string('x', 512)
            },
            idempotencyKey: "idem-chunked-oversized");
        var bodyBytes = await request.Content!.ReadAsByteArrayAsync();
        request.Content = new StreamContent(new ChunkedReadStream(bodyBytes, maxChunkSize: 17));
        request.Content.Headers.ContentType = new("application/json");

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        content.Should().Contain(InternalServiceProblemCodes.RequestBodyTooLarge);
    }

    [Theory]
    [InlineData("corr-step6-valid", true)]
    [InlineData(null, false)]
    [InlineData("this-correlation-id-is-over-sixty-four-characters-long-and-must-not-roundtrip", false)]
    [InlineData("bad\r\nvalue", false)]
    public async Task CatalogSnapshot_CorrelationHeader_IsEchoedOrRegeneratedSafely(
        string? suppliedCorrelationId,
        bool expectPassthrough)
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            correlationId: suppliedCorrelationId);

        if (suppliedCorrelationId is null)
        {
            request.Headers.Remove(InternalServiceWireConstants.CorrelationIdHeaderName);
        }
        else if (!expectPassthrough)
        {
            request.Headers.Remove(InternalServiceWireConstants.CorrelationIdHeaderName);
            request.Headers.TryAddWithoutValidation(
                InternalServiceWireConstants.CorrelationIdHeaderName,
                suppliedCorrelationId);
        }

        var response = await client.SendAsync(request);
        var echoedCorrelationId = response.Headers.GetValues(
            InternalServiceWireConstants.CorrelationIdHeaderName).Single();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        InternalServiceHeaderValueValidator.IsValidCorrelationId(echoedCorrelationId)
            .Should()
            .BeTrue();
        if (expectPassthrough)
        {
            echoedCorrelationId.Should().Be(suppliedCorrelationId);
        }
        else
        {
            echoedCorrelationId.Should().NotBe(suppliedCorrelationId);
        }
    }

    private sealed class ChunkedReadStream : Stream
    {
        private readonly byte[] _buffer;
        private readonly int _maxChunkSize;
        private int _position;

        public ChunkedReadStream(byte[] buffer, int maxChunkSize)
        {
            _buffer = buffer;
            _maxChunkSize = maxChunkSize;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _buffer.Length)
            {
                return 0;
            }

            var toCopy = Math.Min(Math.Min(count, _maxChunkSize), _buffer.Length - _position);
            Array.Copy(_buffer, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override int Read(Span<byte> buffer)
        {
            if (_position >= _buffer.Length)
            {
                return 0;
            }

            var toCopy = Math.Min(Math.Min(buffer.Length, _maxChunkSize), _buffer.Length - _position);
            _buffer.AsSpan(_position, toCopy).CopyTo(buffer);
            _position += toCopy;
            return toCopy;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
