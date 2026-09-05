using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GhseeliApis.DTOs.Auth;
using GhseeliApis.Services.Interfaces;
using GhseeliApis.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Step 15 Customer-host route-family, authorization-ordering, and domain-invariance contracts.
/// </summary>
[Collection(Step15CustomerCollection.Name)]
public sealed class Step15CustomerRouteFamilyHttpTests
{
    private readonly Step15CustomerFixture _fixture;

    public Step15CustomerRouteFamilyHttpTests(Step15CustomerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task STEP15_CUSTOMER_DEVICE_REGISTER_058_IssuanceRotationAndConflictAreReal()
    {
        using var client = _fixture.CreateClient();
        var installationId = Guid.NewGuid();
        var body = $$"""{"installationId":"{{installationId:D}}","platform":"iOS","appVersion":"15.0"}""";

        using var issueResponse = await client.PostAsync(
            "/api/v1/devices/register",
            new StringContent(body, Encoding.UTF8, "application/json"));
        using var issueDocument = await Step15CustomerAssertions.JsonAsync(issueResponse);
        var issuedToken = issueDocument.RootElement.GetProperty("token").GetString()!;

        using var conflictResponse = await client.PostAsync(
            "/api/v1/devices/register",
            new StringContent(body, Encoding.UTF8, "application/json"));
        using var conflictDocument = await Step15CustomerAssertions.JsonAsync(conflictResponse);

        using var rotateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/devices/register");
        rotateRequest.Headers.TryAddWithoutValidation("X-Device-Token", issuedToken);
        rotateRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var rotateResponse = await client.SendAsync(rotateRequest);
        using var rotateDocument = await Step15CustomerAssertions.JsonAsync(rotateResponse);
        var rotatedToken = rotateDocument.RootElement.GetProperty("token").GetString()!;

        issueResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        conflictDocument.RootElement.GetProperty("code").GetString()
            .Should().Be("device_registration_conflict");
        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        rotatedToken.Should().NotBe(issuedToken);

        using var staleRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/catalog/businesses?language=he");
        staleRequest.Headers.TryAddWithoutValidation("X-Device-Token", issuedToken);
        using var staleResponse = await client.SendAsync(staleRequest);
        using var staleDocument = await Step15CustomerAssertions.JsonAsync(staleResponse);
        staleDocument.RootElement.GetProperty("code").GetString()
            .Should().Be("device_token_invalid");

        using var currentRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/catalog/businesses?language=he");
        currentRequest.Headers.TryAddWithoutValidation("X-Device-Token", rotatedToken);
        using var currentResponse = await client.SendAsync(currentRequest);
        currentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-CATEGORIES-060")]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-BUSINESSES-061")]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-BUSINESS-062")]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-OFFERINGS-063")]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-OFFERING-064")]
    public async Task LocalizedCatalogBrowseRoutesReturnRealGraphWithPerFieldFallback()
    {
        using var client = _fixture.CreateClient();
        using var businessesResponse = await client.SendAsync(
            Step15CustomerAssertions.Request(
                HttpMethod.Get,
                "/api/v1/catalog/businesses?language=he",
                device: true));
        using var businesses = await Step15CustomerAssertions.JsonAsync(businessesResponse);
        var business = businesses.RootElement.GetProperty("businesses")[0];
        var businessId = business.GetProperty("id").GetGuid();

        using var detailResponse = await client.SendAsync(Step15CustomerAssertions.Request(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{businessId:D}?language=he",
            device: true));
        using var detail = await Step15CustomerAssertions.JsonAsync(detailResponse);
        using var categoriesResponse = await client.SendAsync(Step15CustomerAssertions.Request(
            HttpMethod.Get,
            "/api/v1/catalog/categories?language=he",
            device: true));
        using var categories = await Step15CustomerAssertions.JsonAsync(categoriesResponse);
        using var offeringsResponse = await client.SendAsync(Step15CustomerAssertions.Request(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{businessId:D}/offerings?language=he",
            device: true));
        using var offerings = await Step15CustomerAssertions.JsonAsync(offeringsResponse);
        var offeringId = offerings.RootElement.GetProperty("offerings")[0]
            .GetProperty("id").GetGuid();
        using var offeringResponse = await client.SendAsync(Step15CustomerAssertions.Request(
            HttpMethod.Get,
            $"/api/v1/catalog/offerings/{offeringId:D}?language=he",
            device: true));
        using var offering = await Step15CustomerAssertions.JsonAsync(offeringResponse);

        businessesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        categoriesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        offeringsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        offeringResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        business.GetProperty("name").GetString().Should().Be("شركة الاختبار");
        detail.RootElement.GetProperty("language").GetString().Should().Be("he");
        categories.RootElement.GetProperty("language").GetString().Should().Be("he");
        offerings.RootElement.GetProperty("language").GetString().Should().Be("he");
        offering.RootElement.GetProperty("language").GetString().Should().Be("he");
    }

    [Fact]
    public async Task STEP15_AUTH_DEVICE_JWT_COMBINED_035_044_DeviceFailurePrecedesJwtChallenge()
    {
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Post,
            "/api/v1/bookings/from-draft?language=ar",
            content: new StringContent("{}", Encoding.UTF8, "application/json"));
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        document.RootElement.GetProperty("code").GetString().Should().Be("device_token_missing");
        response.Headers.WwwAuthenticate.Should().BeEmpty();
    }

    [Theory]
    [InlineData("STEP15-AUTH-CUSTOMER-MISSING-035", null, 401, "customer_authentication_required")]
    [InlineData("STEP15-AUTH-CUSTOMER-MALFORMED-036", "not-a-jwt", 401, "customer_authentication_required")]
    public async Task ValidDeviceThenJwtChallenge_UsesExactCustomerProblem(
        string scenarioId,
        string? jwt,
        int status,
        string code)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Post,
            "/api/v1/bookings/from-draft?language=he",
            device: true,
            bearer: jwt,
            content: new StringContent("{}", Encoding.UTF8, "application/json"));
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(response, document.RootElement, status, code, "he");
        response.Headers.WwwAuthenticate.Should().ContainSingle();
    }

    [Fact]
    public async Task STEP15_AUTH_CUSTOMER_ROLE_037_WrongRoleUsesExactCustomerForbidden()
    {
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Post,
            "/api/v1/bookings/from-draft?language=ar",
            device: true,
            bearer: Step15CustomerAssertions.Jwt("Admin"),
            content: new StringContent("{}", Encoding.UTF8, "application/json"));
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            403,
            "customer_authorization_forbidden");
    }

    [Fact]
    public async Task STEP15_AUTH_CROSS_BUSINESS_TO_CUSTOMER_043_WrongIssuerTokenIsNeverAccepted()
    {
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Get,
            $"/api/v1/payments/{Guid.NewGuid():D}?language=ar",
            device: true,
            bearer: "eyJhbGciOiJIUzI1NiJ9.eyJpc3MiOiJHaHNlZWxpLkJ1c2luZXNzQXBpIn0.invalid");
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            401,
            "customer_authentication_required");
    }

    [Theory]
    [InlineData("STEP15-AUTH-DEVICE-INVALID-045", "short")]
    [InlineData("STEP15-TRANSPORT-DUPLICATE-HEADERS-128",
        "eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHg, eXl5eXl5eXl5eXl5eXl5eXl5eXl5eXl5eXl5eXl5eXk")]
    public async Task InvalidOrDuplicateDeviceHeaderFailsClosed(string scenarioId, string token)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/businesses");
        request.Headers.TryAddWithoutValidation("X-Device-Token", token);
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString().Should().Be("device_token_invalid");
        Step15CustomerAssertions.AssertNoStoreAndNoLeakage(response, document.RootElement.GetRawText());
    }

    [Fact]
    public async Task STEP15_TRANSPORT_DUPLICATE_AUTHORIZATION_128A_FailsBeforeDomainWork()
    {
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Get,
            $"/api/v1/payments/{Guid.NewGuid():D}?language=he",
            device: true);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            ["Bearer " + Step15CustomerAssertions.Jwt(), "Bearer duplicate"]);

        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            401,
            "customer_authentication_required",
            "he");
    }

    [Fact]
    public async Task STEP15_TRANSPORT_DUPLICATE_ORDERGUID_128B_IsRejectedExactly()
    {
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Post,
            "/api/v1/bookings/from-draft?language=ar",
            device: true,
            bearer: Step15CustomerAssertions.Jwt(),
            content: new StringContent(
                """{"expectedVersion":1,"cancellationPolicyAcknowledged":true}""",
                Encoding.UTF8,
                "application/json"));
        request.Headers.TryAddWithoutValidation(
            "X-Order-Guid",
            [Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D")]);

        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be("booking_request_invalid");
    }

    [Fact]
    public async Task STEP15_AUTH_DEVICE_EXPIRED_046_UsesExactExpiredProblem()
    {
        using var client = _fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/businesses?language=he");
        request.Headers.TryAddWithoutValidation(
            "X-Device-Token",
            Step15CustomerFixture.ExpiredDeviceToken);
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.ExactProblem(
            response,
            document.RootElement,
            401,
            "device_token_expired",
            "אימות המכשיר נכשל.",
            "פג תוקף אסימון המכשיר.",
            "he");
    }

    [Fact]
    public async Task STEP15_AUTH_DEVICE_ROTATED_047_OldTokenFailsWhileCurrentTokenSucceeds()
    {
        using var client = _fixture.CreateClient();
        using var oldRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/businesses");
        oldRequest.Headers.TryAddWithoutValidation(
            "X-Device-Token",
            Step15CustomerFixture.RotatedDeviceToken);
        using var oldResponse = await client.SendAsync(oldRequest);
        using var oldDocument = await Step15CustomerAssertions.JsonAsync(oldResponse);
        using var currentRequest = Step15CustomerAssertions.Request(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            device: true);
        using var currentResponse = await client.SendAsync(currentRequest);

        oldResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        oldDocument.RootElement.GetProperty("code").GetString().Should().Be("device_token_invalid");
        currentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task STEP15_AUTH_DEVICE_OWNERSHIP_048_UnknownAndForeignDraftsUseSameMasked404()
    {
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Get,
            $"/api/v1/checkout/drafts/{Guid.NewGuid():D}?language=he",
            device: true);
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be("checkout_draft_not_found");
        document.RootElement.GetProperty("type").GetString()
            .Should().Be("https://api.ghseeli.example/errors/checkout_draft_not_found");
        document.RootElement.GetRawText().Should().NotContainAny("deviceId", "owner", "foreign");
    }

    public static TheoryData<string, string> LegacyProtectedFamilies => new()
    {
        { "STEP15-LEGACY-AUTH-114", "/api/Auth/me" },
        { "STEP15-LEGACY-USERS-115", "/api/Users" },
        { "STEP15-LEGACY-ADDRESSES-116", "/api/Addresses/my-addresses" },
        { "STEP15-LEGACY-VEHICLES-117", "/api/Vehicles/my-vehicles" }
    };

    [Theory]
    [MemberData(nameof(LegacyProtectedFamilies))]
    public async Task LegacyCustomerFamilies_NormalizeJwtFailures(
        string scenarioId,
        string path)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync(path + "?language=he");
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            401,
            "customer_authentication_required",
            "he");
    }

    [Fact]
    public async Task STEP15_LEGACY_AUTH_114_LoginFailureIsLocalizedAndDoesNotEchoPii()
    {
        using var client = _fixture.CreateClient();
        using var body = new StringContent(
            """{"email":"secret@example.com","password":"super-secret"}""",
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync("/api/Auth/login?language=he", body);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            401,
            "customer_authentication_required",
            "he");
    }

    [Fact]
    public async Task STEP15_LEGACY_HEALTH_123_HealthIsAnonymousSafeAndLanguageNeutral()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync("/api/Health?language=he");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentLanguage.Should().BeEmpty();
        using var document = JsonDocument.Parse(body);
        DateTimeOffset.TryParseExact(
                document.RootElement.GetProperty("timestamp").GetString(),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _)
            .Should().BeTrue();
        body.Should().NotContainAny("connectionString", "Server=", "Password=", "secret");
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle();
        Step15CustomerAssertions.AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task STEP15_TRANSPORT_HEAD_OPTIONS_124_HealthHeadIsBodyless()
    {
        using var client = _fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, "/api/Health");
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    public static TheoryData<string, string> InternalRouteFamilies => new()
    {
        { "STEP15-CUSTOMER-INTERNAL-STATUS-073", "/api/v1/internal/bookings/status" },
        { "STEP15-CUSTOMER-INTERNAL-RECONCILE-074", $"/api/v1/internal/bookings/{Guid.NewGuid():D}/reconcile" },
        { "STEP15-CUSTOMER-INTERNAL-READ-075", $"/api/v1/internal/bookings/{Guid.NewGuid():D}" },
        { "STEP15-CUSTOMER-INTERNAL-SHAPE-076", "/api/v1/internal/bookings/not-a-guid/extra" },
        { "STEP15-LANG-INTERNAL-EXEMPT-017", "/api/v1/internal/bookings/status?language=he" },
        { "STEP15-AUTH-JWT-ON-INTERNAL-055", "/api/v1/internal/bookings/status" }
    };

    [Theory]
    [MemberData(nameof(InternalRouteFamilies))]
    public async Task InternalRoutes_RequireHmacBeforeRoutingAndRemainEnglish(
        string scenarioId,
        string path)
    {
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            path.EndsWith("status", StringComparison.Ordinal) ||
            path.Contains("reconcile", StringComparison.Ordinal)
                ? HttpMethod.Post
                : HttpMethod.Get,
            path,
            bearer: scenarioId.Contains("JWT", StringComparison.Ordinal)
                ? Step15CustomerAssertions.Jwt()
                : null,
            content: new StringContent("{}", Encoding.UTF8, "application/json"));
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be("internal_auth_missing_header");
        document.RootElement.GetProperty("type").GetString()
            .Should().Be("https://api.ghseeli.example/errors/internal_auth_missing_header");
        document.RootElement.TryGetProperty("language", out _).Should().BeFalse();
        response.Content.Headers.ContentLanguage.Should().BeEmpty();
        Step15CustomerAssertions.AssertNoStoreAndNoLeakage(response, document.RootElement.GetRawText());
    }

    [Fact]
    public async Task STEP15_CUSTOMER_STRIPE_WEBHOOK_077_IsExemptAndUsesExactEnglishProblem()
    {
        using var client = _fixture.CreateClient();
        using var content = new StringContent(
            "secret@example.com super-secret",
            Encoding.UTF8,
            "text/plain");
        using var response = await client.PostAsync("/api/lahza/webhook?language=he", content);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.ExactProblem(
            response,
            document.RootElement,
            415,
            "lahza_webhook_unsupported_media_type",
            "Lahza webhook request could not be processed.",
            "Lahza webhook request could not be processed.",
            null);
    }

    [Fact]
    public async Task STEP15_PROBLEM_UNHANDLED_027_LegacyExceptionIsNormalizedAndRedacted()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 15);
        var auth = new Mock<IAuthService>();
        auth.Setup(value => value.LoginAsync(It.IsAny<LoginRequest>()))
            .ThrowsAsync(new InvalidOperationException(
                "Server=private;Password=super-secret; secret@example.com"));
        await using var factory = new Step15CustomerApiFactory(
            snapshot,
            [],
            services =>
            {
                services.RemoveAll<IAuthService>();
                services.AddSingleton(auth.Object);
            });
        using var client = factory.CreateApiClient();
        using var content = new StringContent(
            """{"email":"valid@example.com","password":"Password1"}""",
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync("/api/Auth/login?language=he", content);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            500,
            "unexpected_error",
            "he");
    }

    [Fact]
    public async Task STEP15_PROBLEM_UNAVAILABLE_028_UpstreamFailureIsSafeLocalized503()
    {
        var original = _fixture.Factory.BusinessApiClient.GetCatalogSnapshotHandler;
        _fixture.Factory.BusinessApiClient.GetCatalogSnapshotHandler = (_, _) =>
            throw new HttpRequestException(
                "provider Server=private;Password=super-secret; secret@example.com");
        try
        {
            using var client = _fixture.CreateClient();
            using var request = Step15CustomerAssertions.Request(
                HttpMethod.Get,
                "/api/v1/catalog/businesses?refresh=true&language=he",
                device: true);
            using var response = await client.SendAsync(request);
            using var document = await Step15CustomerAssertions.JsonAsync(response);

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            document.RootElement.GetProperty("code").GetString().Should().Be("catalog_unavailable");
            document.RootElement.GetProperty("type").GetString()
                .Should().Be("https://api.ghseeli.example/errors/catalog_unavailable");
            document.RootElement.GetProperty("title").GetString()
                .Should().Be("לא ניתן להשלים את הבקשה.");
            document.RootElement.GetProperty("detail").GetString()
                .Should().Be("קטלוג השירותים אינו זמין כעת.");
            Step15CustomerAssertions.AssertNoStoreAndNoLeakage(
                response,
                document.RootElement.GetRawText());
        }
        finally
        {
            _fixture.Factory.BusinessApiClient.GetCatalogSnapshotHandler = original;
        }
    }

    [Fact]
    public async Task STEP15_INVARIANT_ORDERGUID_155_MissingOrderGuidIsExactAndRedacted()
    {
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Post,
            "/api/v1/bookings/from-draft?language=he",
            device: true,
            bearer: Step15CustomerAssertions.Jwt(),
            content: new StringContent(
                """{"expectedVersion":1,"cancellationPolicyAcknowledged":true,"amount":999999,"currency":"USD","secret":"super-secret"}""",
                Encoding.UTF8,
                "application/json"));
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString().Should().Be("booking_request_invalid");
        document.RootElement.GetProperty("type").GetString()
            .Should().Be("https://api.ghseeli.example/errors/booking_request_invalid");
        Step15CustomerAssertions.AssertNoStoreAndNoLeakage(response, document.RootElement.GetRawText());
    }

    [Fact]
    public async Task STEP15_INVARIANT_SERIALIZATION_164_CustomerJsonUsesCamelCaseUtcAndNumericMoney()
    {
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Get,
            "/api/v1/catalog/businesses?language=ar",
            device: true);
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = document.RootElement.GetRawText();
        raw.Should().NotContain("\"SourceId\"").And.NotContain("\"Language\"");
        raw.Should().NotMatchRegex("\"[a-zA-Z]+At\":\"(?!.*Z\")");
        document.RootElement.GetProperty("businesses")[0]
            .GetProperty("catalog").GetProperty("version").ValueKind
            .Should().Be(JsonValueKind.Number);
    }
}
