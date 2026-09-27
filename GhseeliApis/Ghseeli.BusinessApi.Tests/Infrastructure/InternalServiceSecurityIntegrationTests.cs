using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Models;
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
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-010")]
    public async Task AvailabilityDiscovery_WhenUnsigned_ReturnsUnauthorized()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        const string correlationId = "corr-availability-unsigned";
        client.DefaultRequestHeaders.Add(
            InternalServiceWireConstants.CorrelationIdHeaderName,
            correlationId);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/internal/appointments/availability-discovery",
            new AvailabilityDiscoveryRequest
            {
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                PreferredLocalTime = new TimeOnly(10, 30),
                Candidates =
                [
                    new AvailabilityDiscoveryCompanyCandidate
                    {
                        CompanyId = _factory.CompanyId,
                        BranchIds = [_factory.BranchId]
                    }
                ]
            });

        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should()
            .Be("application/problem+json");
        response.Headers.GetValues(InternalServiceWireConstants.CorrelationIdHeaderName)
            .Should().Equal(correlationId);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(InternalServiceProblemCodes.MissingAuthenticationHeader);
        document.RootElement.GetProperty("type").GetString().Should().Be(
            "https://api.ghseeli.example/errors/internal_auth_missing_header");
        document.RootElement.GetProperty("title").GetString().Should().Be(
            "Internal authentication headers are missing.");
        document.RootElement.GetProperty("status").GetInt32().Should().Be(401);
        document.RootElement.GetProperty("detail").GetString().Should().Be(
            "One or more required internal authentication headers were not supplied.");
        document.RootElement.GetProperty("correlationId").GetString()
            .Should().Be(correlationId);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
                "type", "title", "status", "detail", "code", "correlationId",
                "missingHeaders");
        document.RootElement.GetProperty("missingHeaders").EnumerateArray()
            .Select(value => value.GetString())
            .Should().BeEquivalentTo(
                "serviceId", "timestamp", "nonce", "signature");
        content.Should().NotContainAny(
            "\"results\"", "\"candidates\"", _factory.CompanyId.ToString(),
            _factory.BranchId.ToString(), CatalogApiFactory.InternalServiceActiveSecret);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-009")]
    public async Task AvailabilityDiscovery_WhenSigned_ReturnsVersionedResponse()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        SeedAvailability(date.DayOfWeek);
        var body = new AvailabilityDiscoveryRequest
        {
            Date = date,
            PreferredLocalTime = new TimeOnly(10, 30),
            Candidates =
            [
                new AvailabilityDiscoveryCompanyCandidate
                {
                    CompanyId = _factory.OtherCompanyId,
                    BranchIds = [_factory.OtherBranchId]
                },
                new AvailabilityDiscoveryCompanyCandidate
                {
                    CompanyId = _factory.CompanyId,
                    BranchIds = [_factory.BranchId]
                }
            ]
        };
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/availability-discovery",
            body);

        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadFromJsonAsync<AvailabilityDiscoveryResponse>(
            BusinessCatalogContract.CreateJsonSerializerOptions());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload!.ContractVersion.Should().Be(BusinessCatalogContract.Version);
        payload.Date.Should().Be(body.Date);
        payload.PreferredLocalTime.Should().Be(body.PreferredLocalTime);
        payload.Results.Should().HaveCount(2);
        payload.Results.Select(result => (result.CompanyId, result.BranchId))
            .Should().Equal(
                (_factory.OtherCompanyId, _factory.OtherBranchId),
                (_factory.CompanyId, _factory.BranchId));
        payload.Results.Should().OnlyContain(result =>
            TimeOnly.FromDateTime(result.SlotStartLocal) == body.PreferredLocalTime);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-025")]
    public async Task AvailabilityDiscovery_WhenSignatureIsInvalid_ReturnsExactUnauthorizedProblem()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/availability-discovery",
            ValidAvailabilityRequest());
        var originalServiceId = request.Headers.GetValues(
            InternalServiceWireConstants.ServiceIdHeaderName).Single();
        var originalTimestamp = request.Headers.GetValues(
            InternalServiceWireConstants.TimestampHeaderName).Single();
        var originalNonce = request.Headers.GetValues(
            InternalServiceWireConstants.NonceHeaderName).Single();
        var originalBody = await request.Content!.ReadAsByteArrayAsync();
        var originalSignature = request.Headers.GetValues(
            InternalServiceWireConstants.SignatureHeaderName).Single();
        var mutatedSignature =
            (originalSignature[0] == '0' ? '1' : '0') + originalSignature[1..];
        request.Headers.Remove(InternalServiceWireConstants.SignatureHeaderName);
        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.SignatureHeaderName,
            mutatedSignature);

        mutatedSignature.Should().HaveLength(originalSignature.Length);
        mutatedSignature.Zip(originalSignature)
            .Count(pair => pair.First != pair.Second).Should().Be(1);
        request.Headers.GetValues(InternalServiceWireConstants.ServiceIdHeaderName)
            .Should().ContainSingle(originalServiceId);
        request.Headers.GetValues(InternalServiceWireConstants.TimestampHeaderName)
            .Should().ContainSingle(originalTimestamp);
        request.Headers.GetValues(InternalServiceWireConstants.NonceHeaderName)
            .Should().ContainSingle(originalNonce);
        (await request.Content.ReadAsByteArrayAsync()).Should().Equal(originalBody);

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(InternalServiceProblemCodes.InvalidSignature);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-026")]
    public async Task AvailabilityDiscovery_WhenTimestampIsStale_ReturnsExactUnauthorizedProblem()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/availability-discovery",
            ValidAvailabilityRequest(),
            timestamp: DateTimeOffset.UtcNow.AddMinutes(-10));

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        content.Should().Contain(InternalServiceProblemCodes.TimestampOutOfRange);
        AssertNoAvailabilityData(content);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-027")]
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-028")]
    public async Task AvailabilityDiscovery_WhenNonceIsReplayed_ReturnsExactUnauthorizedProblem()
    {
        _factory.ResetState();
        var before = _factory.ReadState(context =>
            context.AppointmentReservations.Select(item => item.Id).OrderBy(id => id).ToArray());
        using var client = _factory.CreateSecureClient();
        var nonce = $"availability-replay-{Guid.NewGuid():N}";
        using var first = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/availability-discovery",
            ValidAvailabilityRequest(),
            nonce: nonce);
        using var firstResponse = await client.SendAsync(first);
        var afterFirst = _factory.ReadState(context =>
            context.AppointmentReservations.Select(item => item.Id).OrderBy(id => id).ToArray());
        var nonceCountAfterFirst = _factory.ReadState(context =>
            context.InternalServiceNonces.Count(item =>
                item.ServiceId == CatalogApiFactory.InternalServiceId &&
                item.Nonce == nonce));
        using var replay = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/availability-discovery",
            ValidAvailabilityRequest(),
            nonce: nonce);

        using var replayResponse = await client.SendAsync(replay);
        var content = await replayResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        content.Should().Contain(InternalServiceProblemCodes.ReplayNonce);
        var afterReplay = _factory.ReadState(context =>
            context.AppointmentReservations.Select(item => item.Id).OrderBy(id => id).ToArray());
        var nonceCountAfterReplay = _factory.ReadState(context =>
            context.InternalServiceNonces.Count(item =>
                item.ServiceId == CatalogApiFactory.InternalServiceId &&
                item.Nonce == nonce));
        afterFirst.Should().Equal(before);
        afterReplay.Should().Equal(afterFirst);
        nonceCountAfterFirst.Should().Be(1);
        nonceCountAfterReplay.Should().Be(nonceCountAfterFirst);
        AssertNoAvailabilityData(content);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-011")]
    public async Task AvailabilityDiscovery_WhenServiceLacksOperation_ReturnsExactForbiddenProblem()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/availability-discovery",
            ValidAvailabilityRequest(),
            serviceId: CatalogApiFactory.SnapshotOnlyServiceId,
            secret: CatalogApiFactory.SnapshotOnlySecret);

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        content.Should().Contain(InternalServiceProblemCodes.ServiceForbidden);
        AssertNoAvailabilityData(content);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-012")]
    public async Task AvailabilityDiscovery_WhenSignedOverInsecureHttp_ReturnsExactForbiddenProblem()
    {
        _factory.ResetState();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost")
        });
        const string correlationId = "corr-availability-insecure-http";
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/availability-discovery",
            ValidAvailabilityRequest(),
            correlationId: correlationId);

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should()
            .Be("application/problem+json");
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(InternalServiceProblemCodes.HttpsRequired);
        document.RootElement.GetProperty("correlationId").GetString()
            .Should().Be(correlationId);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
                "type", "title", "status", "detail", "code", "correlationId");
        content.Should().NotContainAny(
            "\"results\"", "\"candidates\"", CatalogApiFactory.InternalServiceActiveSecret);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-013")]
    public async Task AvailabilityDiscovery_WhenSignedJsonIsMalformed_ReturnsBadRequestWithoutDomainMutation()
    {
        _factory.ResetState();
        var before = _factory.ReadState(context =>
            context.AppointmentReservations.Count());
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/availability-discovery",
            new InternalServiceRawJson("""{"contractVersion":"v1","candidates":["""));

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        content.Should().NotContainAny(
            CatalogApiFactory.InternalServiceActiveSecret,
            "connection string",
            "stack trace",
            "System.Text.Json",
            """{"contractVersion":"v1","candidates":[""");
        _factory.ReadState(context => context.AppointmentReservations.Count())
            .Should().Be(before);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-INTERNAL-031")]
    public async Task AvailabilityDiscovery_WhenSignedBodyIsOversized_ReturnsPayloadTooLargeWithoutDomainMutation()
    {
        _factory.ResetState();
        var before = _factory.ReadState(context =>
            context.AppointmentReservations.Count());
        using var client = _factory.CreateSecureClient();
        var oversized = JsonSerializer.Serialize(new
        {
            contractVersion = "v1",
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            preferredLocalTime = "10:30:00",
            candidates = new[]
            {
                new
                {
                    companyId = _factory.CompanyId,
                    branchIds = new[] { _factory.BranchId }
                }
            },
            padding = new string('x', InternalServiceWireConstants.MaxRequestBodyBytes)
        });
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/availability-discovery",
            new InternalServiceRawJson(oversized));

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        content.Should().Contain(InternalServiceProblemCodes.RequestBodyTooLarge);
        content.Should().NotContainAny(
            CatalogApiFactory.InternalServiceActiveSecret,
            _factory.CompanyId.ToString(),
            _factory.BranchId.ToString(),
            new string('x', 64),
            "connection string",
            "stack trace",
            "System.");
        _factory.ReadState(context => context.AppointmentReservations.Count())
            .Should().Be(before);
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

    private AvailabilityDiscoveryRequest ValidAvailabilityRequest() => new()
    {
        Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
        PreferredLocalTime = new TimeOnly(10, 30),
        Candidates =
        [
            new AvailabilityDiscoveryCompanyCandidate
            {
                CompanyId = _factory.CompanyId,
                BranchIds = [_factory.BranchId]
            }
        ]
    };

    private void SeedAvailability(DayOfWeek dayOfWeek)
    {
        _factory.MutateState(context =>
        {
            foreach (var branchId in new[] { _factory.CompanyId, _factory.OtherCompanyId }
                .Select(companyId => companyId == _factory.CompanyId
                    ? _factory.BranchId
                    : _factory.OtherBranchId))
            {
                context.BranchAvailabilitySettings.Add(new BranchAvailabilitySettings
                {
                    BranchId = branchId,
                    TimeZoneId = "UTC",
                    BookingHorizonDays = 30,
                    MinimumLeadMinutes = 0,
                    IsActive = true
                });
                context.BranchRecurringSchedules.Add(new BranchRecurringSchedule
                {
                    BranchId = branchId,
                    DayOfWeek = dayOfWeek,
                    StartLocalTime = TimeSpan.FromHours(9),
                    EndLocalTime = TimeSpan.FromHours(12),
                    SlotDurationMinutes = 30,
                    Capacity = 2,
                    IsActive = true
                });
            }
        });
    }

    private void AssertNoAvailabilityData(string content)
    {
        content.Should().NotContainAny(
            "\"results\"",
            "\"candidates\"",
            _factory.CompanyId.ToString(),
            _factory.BranchId.ToString(),
            CatalogApiFactory.InternalServiceActiveSecret);
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
    [Trait("ScenarioId", "FAN-CATEGORY-INTERNAL-015")]
    public async Task CatalogSnapshot_WhenSignatureIsInvalid_DoesNotLeakCategoryOrMutateState()
    {
        _factory.ResetState();
        using var ownerClient = _factory.CreateAuthenticatedClient(
            _factory.OwnerUserId,
            BusinessRoles.Owner);
        using var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = "فئة سرية",
                ImageUrl = "https://cdn.example.test/categories/private.png",
                ColorHex = "#ABCDEF",
                IsActive = true
            });
        var category = await createResponse.Content.ReadFromJsonAsync<ServiceCategoryResponse>();
        var versionBefore = _factory.ReadState(context =>
            context.Companies.Single(company => company.Id == _factory.CompanyId).CatalogVersion);

        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            secret: "wrong-secret-minimum-32-characters___");
        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        content.Should().Contain(InternalServiceProblemCodes.InvalidSignature);
        content.ToLowerInvariant().Should().NotContain(category!.Id.ToString().ToLowerInvariant());
        content.Should().NotContain("فئة سرية");
        content.Should().NotContain("private.png");
        content.Should().NotContain("#ABCDEF");
        _factory.ReadState(context => context.ServiceCategories.Count()).Should().Be(1);
        _factory.ReadState(context =>
                context.Companies.Single(company => company.Id == _factory.CompanyId).CatalogVersion)
            .Should().Be(versionBefore);
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
    public async Task AvailableSlots_WhenSignedRequestIsValid_ReturnsAuthoritativeValidationResponse()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/available-slots",
            new AvailableSlotsRequest
            {
                CompanyId = _factory.CompanyId,
                BranchId = Guid.NewGuid(),
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                Currency = "ILS",
                Items =
                [
                    new AvailableSlotsItemRequest { OfferingId = Guid.NewGuid() }
                ]
            },
            correlationId: "corr-step20-valid");

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, content);
        response.Headers.GetValues(InternalServiceWireConstants.CorrelationIdHeaderName)
            .Single().Should().Be("corr-step20-valid");
        using var document = JsonDocument.Parse(content);
        document.RootElement.GetProperty("valid").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("errors")[0].GetProperty("code")
            .GetString().Should().Be(AppointmentValidationErrorCodes.BranchNotFound);
    }

    [Fact]
    public async Task AvailableSlots_WhenServiceAuthenticationIsMissing_ReturnsUnauthorized()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/internal/appointments/available-slots",
            new AvailableSlotsRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AvailableSlots_ServiceAllowedOnlySlotOperation_Succeeds()
    {
        _factory.ResetState();
        const string serviceId = "slot-only-service";
        const string secret = "SlotOnlyServiceSecret_Minimum32Characters";
        using var slotOnlyFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "InternalServiceAuthentication:Services:2:ServiceId",
                serviceId);
            builder.UseSetting(
                "InternalServiceAuthentication:Services:2:ActiveSecret",
                secret);
            builder.UseSetting(
                "InternalServiceAuthentication:Services:2:AllowedOperations:0",
                InternalServiceOperationNames.AppointmentAvailableSlots);
        });
        using var client = slotOnlyFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/available-slots",
            new AvailableSlotsRequest
            {
                CompanyId = _factory.CompanyId,
                BranchId = Guid.NewGuid(),
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                Currency = "ILS",
                Items =
                [
                    new AvailableSlotsItemRequest { OfferingId = Guid.NewGuid() }
                ]
            },
            serviceId: serviceId,
            secret: secret);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AvailableSlots_WhenNonceIsReplayed_ReturnsConflict()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        var body = new AvailableSlotsRequest
        {
            CompanyId = _factory.CompanyId,
            BranchId = Guid.NewGuid(),
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Currency = "ILS",
            Items =
            [
                new AvailableSlotsItemRequest { OfferingId = Guid.NewGuid() }
            ]
        };
        var nonce = Guid.NewGuid().ToString("N");
        using var first = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/available-slots",
            body,
            nonce: nonce);
        using var replay = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Post,
            "/api/v1/internal/appointments/available-slots",
            body,
            nonce: nonce);

        var firstResponse = await client.SendAsync(first);
        var replayResponse = await client.SendAsync(replay);
        var replayContent = await replayResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        replayContent.Should().Contain(InternalServiceProblemCodes.ReplayNonce);
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
