using FluentAssertions;
using Ghseeli.BusinessApi.Infrastructure;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.BusinessApi.Tests.Infrastructure;
using Ghseeli.IntegrationContracts.Bookings;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Integration;

/// <summary>
/// Freezes Step 15 Business language negotiation, normalized problems, and response headers.
/// </summary>
public class Step15BusinessLocalizationProblemHttpTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public Step15BusinessLocalizationProblemHttpTests(CatalogApiFactory factory) => _factory = factory;

    public static TheoryData<string, string?, string, string> LanguageCases => new()
    {
        { "?language=ar", "he", "ar", "STEP15-LANG-QUERY-AR-001" },
        { "?language=he", "ar", "he", "STEP15-LANG-QUERY-HE-002" },
        { "?language=%20HE%20", "ar", "he", "STEP15-LANG-QUERY-CASE-003" },
        { "", "ar", "ar", "STEP15-LANG-HEADER-AR-007" },
        { "", "he-IL", "he", "STEP15-LANG-HEADER-HE-008" },
        { "", "ar;q=0.4, he;q=0.9", "he", "STEP15-LANG-HEADER-Q-009" },
        { "", "he;q=0.8, ar;q=0.8", "he", "STEP15-LANG-HEADER-TIE-010" },
        { "", "he;q=0", "ar", "STEP15-LANG-HEADER-QZERO-011" },
        { "", "*", "ar", "STEP15-LANG-HEADER-WILDCARD-012" },
        { "", "he;q=broken", "ar", "STEP15-LANG-HEADER-MALFORMED-013" },
        { "", null, "ar", "STEP15-LANG-DEFAULT-015" }
    };

    [Theory]
    [MemberData(nameof(LanguageCases))]
    public async Task ProtectedBusinessRoute_SelectsLanguageDeterministically(
        string query,
        string? acceptLanguage,
        string expectedLanguage,
        string scenarioId)
    {
        using var client = _factory.CreateSecureClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/business/company{query}");
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        var response = await client.SendAsync(request);
        var title = expectedLanguage == "ar"
            ? Step15BusinessHttpTestSupport.ArabicTitle
            : Step15BusinessHttpTestSupport.HebrewTitle;
        var detail = expectedLanguage == "ar"
            ? "مطلوب تسجيل دخول حساب العمل."
            : "נדרש אימות לחשבון העסקי.";

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "business_authentication_required",
            expectedLanguage,
            title,
            detail);
        scenarioId.Should().StartWith("STEP15-");
    }

    public static TheoryData<string, string> InvalidQueryCases => new()
    {
        { "?language=en", "STEP15-LANG-QUERY-UNSUPPORTED-004" },
        { "?language=ar&language=he", "STEP15-LANG-QUERY-DUPLICATE-005" },
        { "?language=", "STEP15-LANG-QUERY-EMPTY-006" },
        { "?language=%20%20", "STEP15-LANG-QUERY-EMPTY-006" }
    };

    [Theory]
    [MemberData(nameof(InvalidQueryCases))]
    public async Task ExplicitInvalidLanguage_IsAuthoritativeAndReturnsArabicSafeProblem(
        string query,
        string scenarioId)
    {
        using var client = _factory.CreateSecureClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("he");

        var response = await client.GetAsync($"/api/v1/business/company{query}");

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "language_invalid",
            "ar",
            Step15BusinessHttpTestSupport.ArabicTitle,
            "اللغة المطلوبة غير مدعومة.");
        scenarioId.Should().StartWith("STEP15-LANG-");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-PROBLEM-MODEL-BINDING-020")]
    public async Task MalformedJson_ReturnsNormalizedDeterministicFieldProblem()
    {
        using var client = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/business/catalog/categories?language=he")
        {
            Content = new StringContent("{\"nameAr\":", Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

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
    [Trait("ScenarioId", "STEP15-PROBLEM-FLUENT-VALIDATION-021")]
    [Trait("ScenarioId", "STEP15-PROBLEM-FIELD-ORDER-034")]
    public async Task MultipleValidationErrors_AreLocalizedCamelCaseOrderedAndDeduplicated()
    {
        using var client = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/business/catalog/offerings?language=he",
            new { categoryId = Guid.Empty, nameAr = "", basePrice = -1, durationMinutes = 0 });
        var payload = await response.Content.ReadAsStringAsync();

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "request_invalid",
            "he",
            Step15BusinessHttpTestSupport.HebrewTitle,
            "הבקשה אינה חוקית.",
            expectFieldErrors: true);
        using var document = JsonDocument.Parse(payload);
        var fields = document.RootElement.GetProperty("fieldErrors");
        var names = fields.EnumerateObject().Select(property => property.Name).ToArray();
        names.Should().BeInAscendingOrder(StringComparer.Ordinal);
        names.Should().OnlyContain(name => !char.IsUpper(name[0]));
        fields.EnumerateObject().Should().OnlyContain(property =>
            property.Value.EnumerateArray().Select(value => value.GetString())
                .SequenceEqual(new[] { "הערך אינו חוקי." }));
    }

    public static TheoryData<HttpMethod, string, HttpContent?, HttpStatusCode, string, string> ProtocolCases => new()
    {
        { HttpMethod.Post, "/api/v1/business/catalog/categories", new StringContent("{}", Encoding.UTF8, "text/plain"), HttpStatusCode.UnsupportedMediaType, "unsupported_media_type", "STEP15-PROBLEM-WRONG-CONTENT-TYPE-022" },
        { HttpMethod.Post, "/api/v1/business/catalog/categories", null, HttpStatusCode.BadRequest, "request_invalid", "STEP15-PROBLEM-MISSING-BODY-023" },
        { HttpMethod.Patch, "/api/v1/business/company", new StringContent("{}", Encoding.UTF8, "application/json"), HttpStatusCode.MethodNotAllowed, "method_not_allowed", "STEP15-PROBLEM-WRONG-METHOD-025" },
        { HttpMethod.Get, "/api/v1/business/route-that-does-not-exist", null, HttpStatusCode.NotFound, "resource_not_found", "STEP15-PROBLEM-UNKNOWN-ROUTE-026" }
    };

    [Theory]
    [MemberData(nameof(ProtocolCases))]
    public async Task ProtocolFailures_UseUnifiedProblemContract(
        HttpMethod method,
        string path,
        HttpContent? content,
        HttpStatusCode expectedStatus,
        string code,
        string scenarioId)
    {
        using var client = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);
        using var request = new HttpRequestMessage(method, $"{path}?language=ar") { Content = content };

        var response = await client.SendAsync(request);

        var details = new Dictionary<string, string>
        {
            ["unsupported_media_type"] = "نوع محتوى الطلب غير مدعوم.",
            ["request_invalid"] = "الطلب غير صالح.",
            ["method_not_allowed"] = "طريقة HTTP غير مسموحة لهذا المسار.",
            ["resource_not_found"] = "المورد المطلوب غير موجود."
        };
        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            expectedStatus,
            code,
            "ar",
            Step15BusinessHttpTestSupport.ArabicTitle,
            details[code],
            code == "request_invalid");
        if (expectedStatus == HttpStatusCode.MethodNotAllowed)
        {
            response.Content.Headers.Allow.Should().NotBeEmpty();
        }
        scenarioId.Should().StartWith("STEP15-PROBLEM-");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-PROBLEM-CORRELATION-ECHO-029")]
    [Trait("ScenarioId", "STEP15-PROBLEM-CACHE-031")]
    [Trait("ScenarioId", "STEP15-HEADERS-LOCALIZED-VARY-165")]
    [Trait("ScenarioId", "STEP15-HEADERS-SECURITY-API-167")]
    public async Task LocalizedResponse_EchoesCorrelationAndCarriesCacheAndSecurityHeaders()
    {
        using var client = _factory.CreateSecureClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/business/company?language=he");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "step15-business-correlation");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle("step15-business-correlation");
        Step15BusinessHttpTestSupport.AssertSecurityHeaders(response);
        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "business_authentication_required",
            "he",
            Step15BusinessHttpTestSupport.HebrewTitle,
            "נדרש אימות לחשבון העסקי.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains spaces")]
    [InlineData("first,second")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [Trait("ScenarioId", "STEP15-PROBLEM-CORRELATION-REPLACE-030")]
    public async Task UnsafeCorrelationId_IsReplaced(string value)
    {
        using var client = _factory.CreateSecureClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/business/company");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", value);

        var response = await client.SendAsync(request);
        var correlation = response.Headers.GetValues("X-Correlation-Id").Single();

        correlation.Should().NotBe(value);
        correlation.Should().NotBeNullOrWhiteSpace();
        correlation.Length.Should().BeLessThanOrEqualTo(128);
        correlation.Should().MatchRegex("^[A-Za-z0-9._:-]+$");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-PROBLEM-CORRELATION-AUTH-HASH-030")]
    public async Task MissingCorrelation_WithAuthorization_UsesPrivateDeterministicHashAndExplicitSafeValueWins()
    {
        using var client = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);
        var authorization = client.DefaultRequestHeaders.Authorization!.ToString();

        using var first = await client.GetAsync("/api/v1/business/company?language=ar");
        using var second = await client.GetAsync("/api/v1/business/company?language=he");
        var firstCorrelation = first.Headers.GetValues("X-Correlation-Id").Single();
        var secondCorrelation = second.Headers.GetValues("X-Correlation-Id").Single();
        var expected = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(authorization)))
            .ToLowerInvariant()[..32];

        firstCorrelation.Should().Be(expected);
        secondCorrelation.Should().Be(expected);
        firstCorrelation.Should().MatchRegex("^[0-9a-f]{32}$");
        firstCorrelation.Should().NotContain(authorization);
        (await first.Content.ReadAsStringAsync()).Should().NotContain(authorization);

        using var explicitRequest = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/business/company?language=ar");
        explicitRequest.Headers.TryAddWithoutValidation(
            "X-Correlation-Id", "step15-explicit-correlation");
        using var explicitResponse = await client.SendAsync(explicitRequest);
        explicitResponse.Headers.GetValues("X-Correlation-Id").Should()
            .ContainSingle("step15-explicit-correlation");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-PROBLEM-BODY-BOUNDARY-024")]
    public async Task JsonBodyOver65536Bytes_ReturnsLocalized413BeforeBinding()
    {
        using var client = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);
        var json = JsonSerializer.Serialize(new { nameAr = new string('ع', 65_537) });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/business/catalog/categories?language=ar")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.RequestEntityTooLarge,
            "request_body_too_large",
            "ar",
            Step15BusinessHttpTestSupport.ArabicTitle,
            "حجم نص الطلب يتجاوز الحد المسموح.");
    }

    [Theory]
    [InlineData(65_536, HttpStatusCode.BadRequest, "request_invalid")]
    [InlineData(65_537, HttpStatusCode.RequestEntityTooLarge, "request_body_too_large")]
    public async Task UnknownLengthHttp2JsonBody_IsBoundedAndAcceptsTheExactLimit(
        int bodyLength,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var client = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);
        var prefix = Encoding.UTF8.GetBytes("{\"nameAr\":\"");
        var suffix = Encoding.UTF8.GetBytes("\"}");
        var body = prefix
            .Concat(Enumerable.Repeat((byte)'a', bodyLength - prefix.Length - suffix.Length))
            .Concat(suffix)
            .ToArray();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/business/catalog/categories?language=ar")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = new UnknownLengthJsonContent(body)
        };

        using var response = await client.SendAsync(request);

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            expectedStatus,
            expectedCode,
            "ar",
            Step15BusinessHttpTestSupport.ArabicTitle,
            expectedCode == "request_body_too_large"
                ? "حجم نص الطلب يتجاوز الحد المسموح."
                : "الطلب غير صالح.",
            expectFieldErrors: expectedStatus == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EmptyUnknownLengthHttp2JsonBody_ReturnsRequestInvalid()
    {
        using var client = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/business/catalog/categories?language=he")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = new UnknownLengthJsonContent([])
        };

        using var response = await client.SendAsync(request);

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
    public async Task Health_IgnoresLanguageQueryAndAcceptLanguage()
    {
        using var client = _factory.CreateSecureClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "he");

        using var response = await client.GetAsync("/api/health?language=en");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.Contains("Content-Language").Should().BeFalse();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        document.RootElement.EnumerateObject().Select(property => property.Name).Should()
            .Equal("status", "service", "timestamp", "version");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-TRANSPORT-HEAD-OPTIONS-124")]
    public async Task Health_TrailingSlashAndHeadHaveTheSameJsonContract()
    {
        using var client = _factory.CreateSecureClient();

        using var trailing = await client.GetAsync("/api/health/");
        using var head = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, "/api/health"));

        trailing.StatusCode.Should().Be(HttpStatusCode.OK);
        trailing.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        trailing.Content.Headers.Contains("Content-Language").Should().BeFalse();
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        (await head.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("ar", "القيمة غير صالحة.")]
    [InlineData("he", "הערך אינו חוקי.")]
    public void NestedFieldErrors_AreCamelCasedPerSegmentOrderedAndDeduplicated(
        string language,
        string frozenMessage)
    {
        const string payload = """
            {
              "errors": {
                "$.Items[10].Options[2].DisplayName": ["unsafe framework text", "duplicate"],
                "Items[2].Options[0].DisplayName": ["another unsafe text"],
                "items[10].options[2].displayName": ["same normalized path"]
              }
            }
            """;
        using var buffer = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var fields = BusinessFieldErrors.Extract(buffer, language);

        fields.Should().BeEquivalentTo(
            new SortedDictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["items[10].options[2].displayName"] = [frozenMessage],
                ["items[2].options[0].displayName"] = [frozenMessage]
            },
            options => options.WithStrictOrdering());
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-PROBLEM-UNHANDLED-027")]
    [Trait("ScenarioId", "STEP15-PROBLEM-REDACTION-032")]
    public async Task UnhandledBusinessFailure_ReturnsSafeRedactedLocalized500()
    {
        using var factory = CreateFailingCompanyFactory(
            new InvalidOperationException("SQL password=secret stack trace System.Data.SqlClient"));
        using var tokenSource = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, "Owner");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization = tokenSource.DefaultRequestHeaders.Authorization;

        var response = await client.GetAsync("/api/v1/business/company?language=he");

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.InternalServerError,
            "unexpected_error",
            "he",
            Step15BusinessHttpTestSupport.HebrewTitle,
            "אירעה שגיאה בלתי צפויה.");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-PROBLEM-UNAVAILABLE-028")]
    public async Task UnavailableBusinessDependency_ReturnsSafeLocalized503()
    {
        using var factory = CreateFailingCompanyFactory(
            new HttpRequestException("provider https://secret.internal failed"));
        using var tokenSource = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, "Owner");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization = tokenSource.DefaultRequestHeaders.Authorization;

        var response = await client.GetAsync("/api/v1/business/company?language=ar");

        await Step15BusinessHttpTestSupport.AssertLocalizedProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            "service_unavailable",
            "ar",
            Step15BusinessHttpTestSupport.ArabicTitle,
            "الخدمة غير متاحة مؤقتًا.");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-PROBLEM-DOMAIN-DETAIL-LOCALIZED-033")]
    public async Task EnglishDomainExceptionDetail_IsDiscardedFromArabicAndHebrewProblems()
    {
        var service = new Mock<IBookingStatusService>();
        service.Setup(candidate => candidate.TransitionAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ThrowsAsync(new BookingStatusRejectedException(
                BookingStatusErrorCodes.TransitionInvalid,
                "Transition from Pending to Completed is not allowed."));
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBookingStatusService>();
                services.AddScoped(_ => service.Object);
            }));
        using var tokenSource = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization = tokenSource.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Idempotency-Key", "step15-localized-domain-detail");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/business/work-orders/{Guid.NewGuid():D}/transitions?language=he",
            new { status = "Completed" });
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        payload.Should().Contain("לא ניתן לבצע את שינוי סטטוס הזמנת העבודה המבוקש.")
            .And.NotContain("Transition")
            .And.NotContain("Pending")
            .And.NotContain("Completed");
    }

    private WebApplicationFactory<Program> CreateFailingCompanyFactory(Exception exception)
    {
        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                var service = new Mock<ICompanyProfileService>();
                service.Setup(candidate => candidate.GetMyCompanyAsync(It.IsAny<Guid>()))
                    .ThrowsAsync(exception);
                services.RemoveAll<ICompanyProfileService>();
                services.AddScoped(_ => service.Object);
            }));
    }

    private sealed class UnknownLengthJsonContent : HttpContent
    {
        private readonly byte[] _body;

        public UnknownLengthJsonContent(byte[] body)
        {
            _body = body;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

    }
}
