using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Step 15 Customer-host language, normalized failure, transport, and header contracts.
/// </summary>
[Collection(Step15CustomerCollection.Name)]
public sealed class Step15CustomerGlobalHttpTests
{
    private readonly Step15CustomerFixture _fixture;

    public Step15CustomerGlobalHttpTests(Step15CustomerFixture fixture) => _fixture = fixture;

    public static TheoryData<string, string, string, string> LanguageCases => new()
    {
        { "STEP15-LANG-QUERY-AR-001", "?language=ar", "he", "ar" },
        { "STEP15-LANG-QUERY-HE-002", "?language=he", "ar", "he" },
        { "STEP15-LANG-QUERY-CASE-003", "?language=%20HE%20", "ar", "he" },
        { "STEP15-LANG-HEADER-AR-007", "", "ar", "ar" },
        { "STEP15-LANG-HEADER-HE-008", "", "he-IL", "he" },
        { "STEP15-LANG-HEADER-Q-009", "", "ar;q=0.2,he;q=0.9", "he" },
        { "STEP15-LANG-HEADER-TIE-010", "", "he;q=0.8,ar;q=0.8", "he" },
        { "STEP15-LANG-HEADER-QZERO-011", "", "he;q=0", "ar" },
        { "STEP15-LANG-HEADER-WILDCARD-012", "", "*", "ar" },
        { "STEP15-LANG-HEADER-MALFORMED-013", "", "-, ;q=banana", "ar" },
        { "STEP15-LANG-DEFAULT-015", "", "", "ar" }
    };

    [Theory]
    [MemberData(nameof(LanguageCases))]
    public async Task LocalizedDeviceProblem_UsesFrozenLanguagePrecedence(
        string scenarioId,
        string query,
        string acceptLanguage,
        string expected)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Get,
            "/api/v1/catalog/businesses" + query,
            acceptLanguage: acceptLanguage);
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);
        Step15CustomerAssertions.ExactProblem(
            response,
            document.RootElement,
            401,
            "device_token_missing",
            expected == "he" ? "אימות המכשיר נכשל." : "فشل التحقق من الجهاز.",
            expected == "he" ? "נדרש אסימון מכשיר." : "رمز الجهاز مطلوب.",
            expected);
        response.Headers.Vary.Should().Contain("Accept-Language");
    }

    [Theory]
    [InlineData("STEP15-LANG-QUERY-UNSUPPORTED-004", "?language=en")]
    [InlineData("STEP15-LANG-QUERY-DUPLICATE-005", "?language=ar&language=he")]
    [InlineData("STEP15-LANG-QUERY-EMPTY-006", "?language=")]
    public async Task ExplicitInvalidLanguage_IsAuthoritativeExactGeneric400(
        string scenarioId,
        string query)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Get,
            "/api/v1/catalog/businesses" + query,
            device: true,
            acceptLanguage: "he");
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            400,
            "language_invalid");
    }

    public static TheoryData<string, string, HttpMethod> InvalidLanguageRouteCases => new()
    {
        { "/api/v1/checkout/drafts", "?language=en", HttpMethod.Post },
        { "/api/v1/pricing/reprice", "?language=", HttpMethod.Post },
        { "/api/v1/bookings/from-draft", "?language=ar&language=he", HttpMethod.Post },
        { $"/api/v1/payments/{Guid.NewGuid():D}", "?language=en", HttpMethod.Get }
    };

    [Theory]
    [MemberData(nameof(InvalidLanguageRouteCases))]
    public async Task EveryDocumentedLocalizedRoute_RejectsInvalidLanguageBeforeAuthOrBinding(
        string path,
        string query,
        HttpMethod method)
    {
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            method,
            path + query,
            acceptLanguage: "he",
            content: method == HttpMethod.Post
                ? new StringContent("{", Encoding.UTF8, "text/plain")
                : null);
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            400,
            "language_invalid",
            "ar");
        response.Headers.WwwAuthenticate.Should().BeEmpty();
    }

    [Theory]
    [InlineData("STEP15-LANG-HEADER-BOUNDARY-014A", 16_384, 401, "device_token_missing")]
    [InlineData("STEP15-LANG-HEADER-OVERSIZE-014B", 16_385, 400, "request_invalid")]
    public async Task AcceptLanguageExactBoundaryHasOneFrozenResponse(
        string scenarioId,
        int length,
        int status,
        string code)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            acceptLanguage: new string('h', length));
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        response.StatusCode.Should().Be((HttpStatusCode)status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        document.RootElement.GetProperty("status").GetInt32().Should().Be(status);
        document.RootElement.GetProperty("code").GetString().Should().Be(code);
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
        document.RootElement.GetRawText().Should().NotContain(new string('h', 1000));
        Step15CustomerAssertions.AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task STEP15_LANG_INVARIANCE_016_ArabicAndHebrewChangePresentationOnly()
    {
        using var client = _fixture.CreateClient();
        using var ar = await client.GetAsync("/api/v1/catalog/businesses?language=ar");
        using var he = await client.GetAsync("/api/v1/catalog/businesses?language=he");
        using var arJson = await Step15CustomerAssertions.JsonAsync(ar);
        using var heJson = await Step15CustomerAssertions.JsonAsync(he);

        ar.StatusCode.Should().Be(he.StatusCode);
        arJson.RootElement.GetProperty("code").GetString()
            .Should().Be(heJson.RootElement.GetProperty("code").GetString());
        arJson.RootElement.GetProperty("type").GetString()
            .Should().Be(heJson.RootElement.GetProperty("type").GetString());
        arJson.RootElement.GetProperty("title").GetString()
            .Should().NotBe(heJson.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task STEP15_CUSTOMER_CONFIGURATION_059_HebrewMissingFieldFallsBackPerFieldToArabic()
    {
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(
            HttpMethod.Get,
            "/api/v1/catalog/businesses?language=he",
            device: true);
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        document.RootElement.GetProperty("businesses")[0].GetProperty("name").GetString()
            .Should().Be("شركة الاختبار");
        response.Content.Headers.ContentLanguage.Should().ContainSingle("he");
        response.Headers.Vary.Should().Contain("Accept-Language");
    }

    [Theory]
    [InlineData("STEP15-PROBLEM-MODEL-BINDING-020", "POST", "/api/v1/devices/register", "{")]
    [InlineData("STEP15-PROBLEM-MISSING-BODY-023", "POST", "/api/v1/devices/register", "")]
    public async Task InvalidBodies_ReturnExactRequestInvalid(
        string scenarioId,
        string method,
        string path,
        string body)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(
            Step15CustomerAssertions.Request(new HttpMethod(method), path, content: content));
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            400,
            "request_invalid");
        if (document.RootElement.TryGetProperty("fieldErrors", out var fields))
        {
            fields.ToString().Should().NotContainAny("System.", "JsonException", body);
        }
    }

    [Fact]
    public async Task STEP15_PROBLEM_WRONG_CONTENT_TYPE_022_ReturnsExact415BeforeHandler()
    {
        using var client = _fixture.CreateClient();
        using var content = new StringContent("secret@example.com super-secret", Encoding.UTF8, "text/plain");
        using var response = await client.PostAsync("/api/v1/devices/register?language=he", content);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            415,
            "unsupported_media_type",
            "he");
    }

    [Theory]
    [InlineData("STEP15-PROBLEM-BODY-BOUNDARY-024", 65_537, true)]
    [InlineData("STEP15-PROBLEM-BODY-BOUNDARY-024", 65_537, false)]
    public async Task OversizedKnownLengthAndChunkedBodies_ReturnExact413(
        string scenarioId,
        int bytes,
        bool knownLength)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        HttpContent content = knownLength
            ? new ByteArrayContent(Encoding.UTF8.GetBytes(new string('x', bytes)))
            : new Step15UnknownLengthContent(Encoding.UTF8.GetBytes(new string('x', bytes)));
        using (content)
        {
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await client.PostAsync("/api/v1/devices/register", content);
            using var document = await Step15CustomerAssertions.JsonAsync(response);
            Step15CustomerAssertions.GenericProblem(
                response,
                document.RootElement,
                413,
                "request_body_too_large");
        }
    }

    [Fact]
    public async Task STEP15_PROBLEM_WRONG_METHOD_025_ReturnsExact405AndAllow()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.DeleteAsync("/api/v1/devices/register");
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            405,
            "method_not_allowed");
        response.Content.Headers.Allow.Should().Contain("POST");
    }

    [Fact]
    public async Task STEP15_PROBLEM_UNKNOWN_ROUTE_026_ReturnsSafeExact404()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync("/api/v1/not-a-real-route/secret@example.com");
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.GenericProblem(
            response,
            document.RootElement,
            404,
            "resource_not_found");
    }

    [Theory]
    [InlineData("STEP15-PROBLEM-CORRELATION-ECHO-029", "step15-safe-correlation")]
    [InlineData("STEP15-PROBLEM-CORRELATION-REPLACE-030", "")]
    [InlineData("STEP15-PROBLEM-CORRELATION-REPLACE-030",
        "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task Correlation_IsEchoedOrSafelyReplaced(string scenarioId, string supplied)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/absent");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", supplied);
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);
        var actual = response.Headers.GetValues("X-Correlation-Id").Single();

        actual.Should().NotBeNullOrWhiteSpace();
        actual.Length.Should().BeLessThanOrEqualTo(64);
        document.RootElement.GetProperty("correlationId").GetString().Should().Be(actual);
        if (supplied == "step15-safe-correlation")
        {
            actual.Should().Be(supplied);
        }
        else
        {
            actual.Should().NotBe(supplied);
        }
    }

    [Theory]
    [InlineData("STEP15-PROBLEM-CACHE-031", "/api/v1/absent")]
    [InlineData("STEP15-HEADERS-SECURITY-API-167", "/api/v1/absent")]
    [InlineData("STEP15-HEADERS-PIPELINE-FALLBACK-171", "/api/v1/devices/register")]
    [InlineData("STEP15-HEADERS-HEALTH-172", "/api/Health")]
    public async Task EveryResponseClass_HasFrozenCacheAndSecurityHeaders(
        string scenarioId,
        string path)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync(path);

        Step15CustomerAssertions.AssertSecurityHeaders(response);
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task STEP15_HEADERS_LOCALIZED_VARY_165_LocalizedProblemMergesHeaders()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync("/api/v1/catalog/businesses?language=he");

        response.Content.Headers.ContentLanguage.Should().Equal("he");
        response.Headers.Vary.Should().Equal("Accept-Language");
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle();
        response.Headers.CacheControl!.ToString().Should().Be("no-store");
        Step15CustomerAssertions.AssertSecurityHeaders(response);
    }

    public static TheoryData<string, HttpMethod, string, bool, int, string[]> HeadOptionsCases => new()
    {
        {
            "STEP15-TRANSPORT-HEAD-HEALTH-124A", HttpMethod.Head, "/api/Health",
            false, 200, []
        },
        {
            "STEP15-TRANSPORT-HEAD-CATALOG-124B", HttpMethod.Head,
            "/api/v1/catalog/businesses?language=he", true, 405, ["GET"]
        },
        {
            "STEP15-TRANSPORT-OPTIONS-DEVICE-124C", HttpMethod.Options,
            "/api/v1/devices/register", false, 405, ["POST"]
        },
        {
            "STEP15-TRANSPORT-OPTIONS-CATALOG-124D", HttpMethod.Options,
            "/api/v1/catalog/businesses", false, 405, ["GET"]
        },
        {
            "STEP15-TRANSPORT-OPTIONS-PAYMENT-124E", HttpMethod.Options,
            "/api/v1/payments/intents", false, 405, ["POST"]
        }
    };

    [Theory]
    [MemberData(nameof(HeadOptionsCases))]
    public async Task HeadAndOptionsHaveExactBodyAndAllowContracts(
        string scenarioId,
        HttpMethod method,
        string path,
        bool device,
        int expectedStatus,
        string[] expectedAllow)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var request = Step15CustomerAssertions.Request(method, path, device);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be((HttpStatusCode)expectedStatus);
        response.Content.Headers.Allow.Should().BeEquivalentTo(expectedAllow);
        if (method == HttpMethod.Head)
        {
            (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
        }
        else
        {
            using var document = await Step15CustomerAssertions.JsonAsync(response);
            document.RootElement.GetProperty("code").GetString()
                .Should().Be("method_not_allowed");
            document.RootElement.GetProperty("status").GetInt32().Should().Be(405);
        }
        Step15CustomerAssertions.AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task STEP15_TRANSPORT_TRAILING_SLASH_125_CatalogCanonicalAndSlashAreEquivalent()
    {
        using var client = _fixture.CreateClient();
        using var canonical = await client.SendAsync(Step15CustomerAssertions.Request(
            HttpMethod.Get, "/api/v1/catalog/businesses?language=he", device: true));
        using var slash = await client.SendAsync(Step15CustomerAssertions.Request(
            HttpMethod.Get, "/api/v1/catalog/businesses/?language=he", device: true));
        var canonicalBody = await canonical.Content.ReadAsStringAsync();
        var slashBody = await slash.Content.ReadAsStringAsync();

        slash.StatusCode.Should().Be(canonical.StatusCode);
        slash.Content.Headers.ContentType!.MediaType
            .Should().Be(canonical.Content.Headers.ContentType!.MediaType);
        using var canonicalJson = JsonDocument.Parse(canonicalBody);
        using var slashJson = JsonDocument.Parse(slashBody);
        slashJson.RootElement.GetProperty("businesses").GetArrayLength()
            .Should().Be(canonicalJson.RootElement.GetProperty("businesses").GetArrayLength());
    }

    [Theory]
    [InlineData("STEP15-TRANSPORT-ACCEPT-JSON-126A", "application/json")]
    [InlineData("STEP15-TRANSPORT-ACCEPT-PROBLEM-126B", "application/problem+json")]
    [InlineData("STEP15-TRANSPORT-ACCEPT-WILDCARD-126C", "*/*")]
    public async Task AcceptNegotiationNeverChangesFrozenProblemMediaType(
        string scenarioId,
        string accept)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/catalog/businesses?language=he");
        request.Headers.Accept.ParseAdd(accept);
        using var response = await client.SendAsync(request);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        Step15CustomerAssertions.ExactProblem(
            response,
            document.RootElement,
            401,
            "device_token_missing",
            "אימות המכשיר נכשל.",
            "נדרש אסימון מכשיר.",
            "he");
    }

    [Fact]
    public async Task STEP15_TRANSPORT_UTF8_127_ArabicAndHebrewAreLiteralUtf8NotEscaped()
    {
        using var client = _fixture.CreateClient();
        using var ar = await client.GetAsync("/api/v1/catalog/businesses?language=ar");
        using var he = await client.GetAsync("/api/v1/catalog/businesses?language=he");
        var arBody = await ar.Content.ReadAsStringAsync();
        var heBody = await he.Content.ReadAsStringAsync();

        arBody.Should().Contain("فشل التحقق من الجهاز.")
            .And.NotContain("\\u");
        heBody.Should().Contain("אימות המכשיר נכשל.")
            .And.NotContain("\\u");
        ar.Content.Headers.ContentType!.CharSet.Should().Be("utf-8");
        he.Content.Headers.ContentType!.CharSet.Should().Be("utf-8");
    }

    [Theory]
    [InlineData("STEP15-TRANSPORT-QUERY-PERCENT-129A", "?language=%68%65", "he")]
    [InlineData("STEP15-TRANSPORT-QUERY-PLUS-129B", "?language=%20he%20", "he")]
    public async Task PercentEncodedQueryValuesDecodeOnceAndRemainBounded(
        string scenarioId,
        string query,
        string expectedLanguage)
    {
        _ = scenarioId;
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync("/api/v1/catalog/businesses" + query);
        using var document = await Step15CustomerAssertions.JsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("language").GetString().Should().Be(expectedLanguage);
        document.RootElement.GetProperty("code").GetString().Should().Be("device_token_missing");
    }
}

internal sealed class Step15UnknownLengthContent(byte[] bytes) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        stream.WriteAsync(bytes).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
