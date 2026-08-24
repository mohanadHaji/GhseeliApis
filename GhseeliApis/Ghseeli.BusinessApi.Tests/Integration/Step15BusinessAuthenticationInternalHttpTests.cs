using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Tests.Infrastructure;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Integration;

/// <summary>
/// Freezes Business JWT isolation and internal HMAC-only route behavior for Step 15.
/// </summary>
public class Step15BusinessAuthenticationInternalHttpTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public Step15BusinessAuthenticationInternalHttpTests(CatalogApiFactory factory) => _factory = factory;

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-AUTH-REGISTER-078")]
    public async Task RegisterOwner_InvalidBilingualPayload_ReturnsLocalizedFieldProblemWithoutRoleInput()
    {
        using var client = _factory.CreateSecureClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/business/auth/register-owner?language=he",
            new
            {
                email = "not-an-email",
                password = "short",
                fullName = "",
                companyNameAr = "",
                companyNameHe = "   ",
                role = "Admin"
            });

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "request_invalid",
            "he",
            Step15BusinessHttpTestSupport.HebrewTitle,
            "הבקשה אינה חוקית.",
            expectFieldErrors: true);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-AUTH-REGISTER-078")]
    [Trait("ScenarioId", "STEP15-AUTH-ANONYMOUS-EXEMPTIONS-057")]
    public async Task RegisterOwner_AnonymousSuccessIssuesOwnerOnlyBusinessToken()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        var unique = Guid.NewGuid().ToString("N");

        var response = await client.PostAsJsonAsync(
            "/api/v1/business/auth/register-owner?language=ar",
            new
            {
                email = $"step15-{unique}@example.invalid",
                password = "ValidPassword1!",
                fullName = "Step Fifteen",
                companyNameAr = "شركة جديدة",
                companyNameHe = " "
            });
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        var roles = document.RootElement.GetProperty("roles")
            .EnumerateArray().Select(role => role.GetString()).ToArray();
        roles.Should().ContainSingle(BusinessRoles.Owner);
        roles.Should().NotContain(BusinessRoles.Admin);
        document.RootElement.GetProperty("token").GetString().Should().NotBeNullOrWhiteSpace();
        response.Content.Headers.ContentLanguage.Should().ContainSingle("ar");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-AUTH-LOGIN-079")]
    public async Task Login_InvalidCredentials_ReturnsSafeLocalizedProblemAndNeverEchoesCredentials()
    {
        using var client = _factory.CreateSecureClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/business/auth/login?language=ar",
            new { email = "missing@example.invalid", password = "Password1!" });
        var payload = await response.Content.ReadAsStringAsync();

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "business_authentication_required",
            "ar",
            Step15BusinessHttpTestSupport.ArabicTitle,
            "مطلوب تسجيل دخول حساب العمل.");
        payload.Should().NotContain("missing@example.invalid");
        payload.Should().NotContain("Password1!");
    }

    public static TheoryData<string, string, string> MissingJwtCases => new()
    {
        { "/api/v1/business/company", "STEP15-AUTH-BUSINESS-MISSING-038", "ar" },
        { "/api/v1/business/catalog/categories", "STEP15-AUTH-BUSINESS-MISSING-038", "he" }
    };

    [Theory]
    [MemberData(nameof(MissingJwtCases))]
    public async Task MissingBusinessJwt_ReturnsExactLocalizedAuthenticationProblem(
        string path,
        string scenarioId,
        string language)
    {
        using var client = _factory.CreateSecureClient();

        var response = await client.GetAsync($"{path}?language={language}");

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "business_authentication_required",
            language,
            language == "ar" ? Step15BusinessHttpTestSupport.ArabicTitle : Step15BusinessHttpTestSupport.HebrewTitle,
            language == "ar" ? "مطلوب تسجيل دخول حساب العمل." : "נדרש אימות לחשבון העסקי.");
        response.Headers.WwwAuthenticate.Should().ContainSingle()
            .Which.Scheme.Should().Be("Bearer");
        scenarioId.Should().Be("STEP15-AUTH-BUSINESS-MISSING-038");
    }

    [Theory]
    [InlineData("not-a-jwt", "STEP15-AUTH-BUSINESS-MALFORMED-039")]
    [InlineData("customer.jwt.must.not.cross.host", "STEP15-AUTH-CROSS-CUSTOMER-TO-BUSINESS-042")]
    public async Task InvalidOrCustomerJwt_IsRejectedWithoutTokenEcho(string token, string scenarioId)
    {
        using var client = _factory.CreateSecureClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/business/company?language=ar");
        var payload = await response.Content.ReadAsStringAsync();

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "business_authentication_required",
            "ar",
            Step15BusinessHttpTestSupport.ArabicTitle,
            "مطلوب تسجيل دخول حساب العمل.");
        payload.Should().NotContain(token);
        scenarioId.Should().StartWith("STEP15-AUTH-");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-AUTH-BUSINESS-ROLE-040")]
    public async Task EmployeeOnOwnerMutation_ReturnsLocalizedForbidden()
    {
        using var client = _factory.CreateAuthenticatedClient(_factory.EmployeeUserId, BusinessRoles.Employee);

        var response = await client.PutAsJsonAsync(
            "/api/v1/business/company?language=he",
            new { nameAr = "شركة", nameHe = "חברה" });

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.Forbidden,
            "business_authorization_forbidden",
            "he",
            Step15BusinessHttpTestSupport.HebrewTitle,
            "לחשבון העסקי אין הרשאה לבצע בקשה זו.");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-AUTH-BUSINESS-ASSIGNMENT-041")]
    public async Task ForeignCompanyResource_IsIndistinguishableFromMissingResource()
    {
        _factory.ResetState();
        using var owner = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);
        using var otherOwner = _factory.CreateAuthenticatedClient(_factory.OtherOwnerUserId, BusinessRoles.Owner);
        var createdResponse = await owner.PostAsJsonAsync(
            "/api/v1/business/catalog/categories",
            new { nameAr = "خاص", nameHe = "פרטי", displayOrder = 0, isActive = true });
        var created = await createdResponse.Content.ReadFromJsonAsync<ServiceCategoryResponse>();

        var foreign = await otherOwner.GetAsync(
            $"/api/v1/business/catalog/categories/{created!.Id}?language=ar");
        var missing = await otherOwner.GetAsync(
            $"/api/v1/business/catalog/categories/{Guid.NewGuid()}?language=ar");

        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().Be(await missing.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-AUTH-HMAC-MISSING-049")]
    [Trait("ScenarioId", "STEP15-LANG-INTERNAL-EXEMPT-017")]
    public async Task InternalRoute_MissingHmacIsEnglishAndIgnoresLanguageSelectors()
    {
        using var client = _factory.CreateSecureClient();

        var response = await client.GetAsync(
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}&language=he");

        await Step15BusinessHttpTestSupport.AssertEnglishInternalProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "internal_auth_missing_header");
        (await response.Content.ReadAsStringAsync()).Should()
            .NotContain("X-Ghseeli-Signature");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-AUTH-HMAC-HTTPS-054")]
    public async Task InternalRoute_InsecureHttpReturnsExactHttpsRequiredProblem()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost")
        });

        var response = await client.GetAsync("/api/v1/internal/catalog/snapshot");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        problem.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "type", "title", "status", "detail", "code", "correlationId");
        problem.GetProperty("code").GetString().Should().Be("https_required");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-AUTH-HMAC-SIGNATURE-050")]
    public async Task InternalRoute_InvalidSignatureReturnsExactEnglishProblem()
    {
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            secret: "wrong-secret-minimum-32-characters___");

        var response = await client.SendAsync(request);

        await Step15BusinessHttpTestSupport.AssertEnglishInternalProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "internal_auth_invalid_signature");
    }

    [Theory]
    [InlineData("X-Ghseeli-Service-Id")]
    [InlineData("X-Ghseeli-Timestamp")]
    [InlineData("X-Ghseeli-Nonce")]
    [InlineData("X-Ghseeli-Signature")]
    [Trait("ScenarioId", "STEP15-AUTH-HMAC-DUPLICATE-050")]
    public async Task InternalRoute_DuplicateSecurityHeaderIsRejectedExactly(
        string headerName)
    {
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}");
        request.Headers.TryAddWithoutValidation(headerName, "duplicate-value")
            .Should().BeTrue();

        using var response = await client.SendAsync(request);

        await Step15BusinessHttpTestSupport.AssertEnglishInternalProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "internal_auth_invalid_signature");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-AUTH-HMAC-TIME-051")]
    public async Task InternalRoute_StaleTimestampIsRejectedWithoutConsumingIdempotency()
    {
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            timestamp: DateTimeOffset.UtcNow.AddMinutes(-10));

        var response = await client.SendAsync(request);

        await Step15BusinessHttpTestSupport.AssertEnglishInternalProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            InternalServiceProblemCodes.TimestampOutOfRange);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-AUTH-HMAC-NONCE-052")]
    public async Task InternalRoute_ReplayedNonceIsRejectedWithoutSecondDomainExecution()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        var nonce = Guid.NewGuid().ToString("N");
        using var first = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client, HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            nonce: nonce);
        using var replay = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client, HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            nonce: nonce);

        (await client.SendAsync(first)).StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await client.SendAsync(replay);

        await Step15BusinessHttpTestSupport.AssertEnglishInternalProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            InternalServiceProblemCodes.ReplayNonce);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-AUTH-HMAC-OPERATION-053")]
    public async Task InternalRoute_ServiceWithoutGrantReturnsEnglishForbidden()
    {
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
            idempotencyKey: "step15-operation-grant",
            serviceId: CatalogApiFactory.SnapshotOnlyServiceId,
            secret: CatalogApiFactory.SnapshotOnlySecret);

        var response = await client.SendAsync(request);

        await Step15BusinessHttpTestSupport.AssertEnglishInternalProblemAsync(
            response,
            HttpStatusCode.Forbidden,
            "internal_service_forbidden");
    }

    [Theory]
    [InlineData("business")]
    [InlineData("customer")]
    [Trait("ScenarioId", "STEP15-AUTH-JWT-ON-INTERNAL-055")]
    public async Task InternalRoute_RejectsJwtAlternatives(string tokenKind)
    {
        using var client = tokenKind == "business"
            ? _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner)
            : _factory.CreateSecureClient();
        if (tokenKind == "customer")
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "customer.jwt.not.business.hmac");
        }

        var response = await client.GetAsync(
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}");

        await Step15BusinessHttpTestSupport.AssertEnglishInternalProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "internal_auth_missing_header");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-AUTH-HMAC-ON-USER-056")]
    public async Task ValidHmacHeaders_DoNotAuthorizeBusinessUserRoute()
    {
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client, HttpMethod.Get, "/api/v1/business/company");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-INTERNAL-CATALOG-110")]
    [Trait("ScenarioId", "STEP15-PROBLEM-CORRELATION-ECHO-029")]
    public async Task CatalogSnapshot_ValidHmacSucceedsAndEchoesSafeCorrelation()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        using var request = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/internal/catalog/snapshot?companyId={_factory.CompanyId}",
            correlationId: "step15-internal-correlation");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle("step15-internal-correlation");
        response.Content.Headers.ContentLanguage.Should().BeEmpty();
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-INTERNAL-VALIDATE-111")]
    [Trait("ScenarioId", "STEP15-INVARIANT-IDEMPOTENCY-154")]
    public async Task ValidateAppointment_ReplayWithDifferentBodyReturnsEnglishIdempotencyConflict()
    {
        _factory.ResetState();
        using var client = _factory.CreateSecureClient();
        const string key = "step15-validate-idempotency";
        using var first = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client, HttpMethod.Post, "/api/v1/internal/appointments/validate",
            InvalidValidationRequest(Guid.NewGuid()), key);
        using var second = await InternalServiceTestRequestFactory.CreateSignedRequestAsync(
            client, HttpMethod.Post, "/api/v1/internal/appointments/validate",
            InvalidValidationRequest(Guid.NewGuid()), key);

        _ = await client.SendAsync(first);
        var response = await client.SendAsync(second);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentLanguage.Should().BeEmpty();
        Step15BusinessHttpTestSupport.AssertRedacted(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-INTERNAL-RESERVE-112")]
    [Trait("ScenarioId", "STEP15-INVARIANT-SELECTION-156")]
    [Trait("ScenarioId", "STEP15-INVARIANT-PRICING-157")]
    public async Task ReservationCreate_RequiresHmacEvenWhenBusinessJwtIsValid()
    {
        using var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        var response = await client.PostAsJsonAsync(
            "/api/v1/internal/reservations",
            new { companyId = _factory.CompanyId });

        await Step15BusinessHttpTestSupport.AssertEnglishInternalProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "internal_auth_missing_header");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-INTERNAL-RESERVATION-READ-113")]
    public async Task ReservationRead_RequiresHmacAndMasksReference()
    {
        using var client = _factory.CreateSecureClient();
        var reference = Guid.NewGuid();

        var response = await client.GetAsync($"/api/v1/internal/reservations/{reference}");
        var payload = await response.Content.ReadAsStringAsync();

        await Step15BusinessHttpTestSupport.AssertEnglishInternalProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "internal_auth_missing_header");
        payload.Should().NotContain(reference.ToString());
    }

    private static ValidateAppointmentRequest InvalidValidationRequest(Guid branchId) => new()
    {
        BranchId = branchId,
        OfferingId = Guid.NewGuid(),
        RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddDays(1),
        Currency = "ILS"
    };
}
