using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Red contract tests for the frozen Step 15 Business OpenAPI document.
/// </summary>
public sealed class Step15SwaggerBusinessContractTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public Step15SwaggerBusinessContractTests(CatalogApiFactory factory) => _factory = factory;

    [Fact(DisplayName = "STEP15-SWAGGER-BUSINESS-ROUTES-131 STEP15-LEGACY-ABSENT-114-123 STEP15-SWAGGER-STATUS-CUSTOMER-137")]
    [Trait("ScenarioId", "STEP15-LEGACY-AUTH-114")]
    [Trait("ScenarioId", "STEP15-LEGACY-USERS-115")]
    [Trait("ScenarioId", "STEP15-LEGACY-ADDRESSES-116")]
    [Trait("ScenarioId", "STEP15-LEGACY-VEHICLES-117")]
    [Trait("ScenarioId", "STEP15-LEGACY-BOOKINGS-118")]
    [Trait("ScenarioId", "STEP15-LEGACY-COMPANIES-119")]
    [Trait("ScenarioId", "STEP15-LEGACY-SERVICES-120")]
    [Trait("ScenarioId", "STEP15-LEGACY-SERVICEOPTIONS-121")]
    [Trait("ScenarioId", "STEP15-LEGACY-PAYMENTS-122")]
    [Trait("ScenarioId", "STEP15-LEGACY-HEALTH-123")]
    [Trait("ScenarioId", "STEP15-SWAGGER-STATUS-CUSTOMER-137")]
    public async Task Business_document_has_exact_owned_operation_inventory()
    {
        using var document = await GetSwaggerAsync();
        var actual = Operations(document.RootElement)
            .Select(operation => $"{operation.Method} {operation.Path}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        actual.Should().Equal(BusinessOperations.Order(StringComparer.Ordinal),
            "STEP15-SWAGGER-BUSINESS-ROUTES-131 requires 47 controller operations plus Business health exactly once");
        actual.Should().NotContain(operation =>
                operation.Contains("/api/stripe", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("/api/Auth", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("/api/Users", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("/api/Addresses", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("/api/Vehicles", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("/api/Bookings", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("/api/Companies", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("/api/Services", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("/api/ServiceOptions", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("/api/Health/db", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("/api/v1/payments", StringComparison.OrdinalIgnoreCase),
            "STEP15-SWAGGER-BUSINESS-ROUTES-131 excludes Customer and Stripe routes");

        foreach (var operation in Operations(document.RootElement))
        {
            RequiredText(operation.Value, "operationId", operation);
            RequiredText(operation.Value, "summary", operation);
            RequiredText(operation.Value, "description", operation);
            operation.Value.GetProperty("tags").GetArrayLength().Should().BeGreaterThan(0,
                $"STEP15-SWAGGER-BUSINESS-ROUTES-131 requires tags on {operation.Method} {operation.Path}");
        }
    }

    [Fact(DisplayName = "STEP15-SWAGGER-BUSINESS-SECURITY-133 STEP15-SWAGGER-NO-GLOBAL-AUTH-134 STEP15-AUTH-ANONYMOUS-EXEMPTIONS-057")]
    public async Task Business_security_is_per_operation_with_complete_HMAC_quartet()
    {
        using var document = await GetSwaggerAsync();
        var root = document.RootElement;

        root.TryGetProperty("security", out _).Should().BeFalse(
            "STEP15-SWAGGER-NO-GLOBAL-AUTH-134 forbids global auth");
        AssertHttpBearer(root, "BusinessBearer");
        AssertApiKey(root, "HmacServiceId", "X-Ghseeli-Service-Id");
        AssertApiKey(root, "HmacTimestamp", "X-Ghseeli-Timestamp");
        AssertApiKey(root, "HmacNonce", "X-Ghseeli-Nonce");
        AssertApiKey(root, "HmacSignature", "X-Ghseeli-Signature");

        AssertNoSecurity(root, "/api/v1/business/auth/register-owner", "post");
        AssertNoSecurity(root, "/api/v1/business/auth/login", "post");
        AssertNoSecurity(root, "/api/health", "get");

        foreach (var operation in Operations(root))
        {
            if (operation.Path.StartsWith("/api/v1/internal/", StringComparison.Ordinal))
            {
                SecurityNames(operation.Value).Should().Equal(
                    ["HmacServiceId", "HmacTimestamp", "HmacNonce", "HmacSignature"],
                    $"STEP15-SWAGGER-BUSINESS-SECURITY-133 requires one combined HMAC requirement on {operation.Method} {operation.Path}");
            }
            else if (!operation.Path.Contains("/auth/", StringComparison.Ordinal) &&
                     operation.Path != "/api/health")
            {
                SecurityNames(operation.Value).Should().Equal(["BusinessBearer"],
                    $"STEP15-SWAGGER-BUSINESS-SECURITY-133 requires Business JWT on {operation.Method} {operation.Path}");
                operation.Value.GetProperty("description").GetString().Should()
                    .MatchRegex("(?i)(assignment|company|branch|owner|employee|admin)",
                        "STEP15-SWAGGER-BUSINESS-SECURITY-133 requires assignment/policy documentation");
            }
        }
    }

    [Fact(DisplayName = "STEP15-SWAGGER-IDEMPOTENCY-148 STEP15-SWAGGER-LANGUAGE-135")]
    public async Task Business_headers_are_operation_accurate_and_bounded()
    {
        using var document = await GetSwaggerAsync();
        var root = document.RootElement;

        AssertHeader(root, "/api/v1/internal/appointments/validate", "post",
            "Idempotency-Key", true, 128);
        AssertHeader(root, "/api/v1/internal/reservations", "post",
            "Idempotency-Key", true, 128);
        AssertHeader(root, "/api/v1/business/work-orders/{id}/transitions", "post",
            "Idempotency-Key", true, 128);
        AssertHeader(root, "/api/v1/business/admin/booking-status-outbox/{eventId}/requeue",
            "post", "Idempotency-Key", true, 128);

        foreach (var operation in Operations(root)
                     .Where(operation => operation.Path.StartsWith(
                         "/api/v1/business/", StringComparison.Ordinal)))
        {
            AssertParameter(operation.Value, "language", "query", false)
                .GetProperty("schema").GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()).Should().Equal(["ar", "he"],
                    $"STEP15-SWAGGER-LANGUAGE-135 requires ar/he on {operation.Path}");
            AssertParameter(operation.Value, "Accept-Language", "header", false);
            AssertParameter(operation.Value, "X-Correlation-Id", "header", false);
        }

        Operations(root).Where(operation =>
                operation.Path.StartsWith("/api/v1/internal/", StringComparison.Ordinal))
            .Should().OnlyContain(operation =>
                    !operation.Value.GetProperty("parameters").EnumerateArray().Any(parameter =>
                        parameter.GetProperty("name").GetString() == "language" ||
                        parameter.GetProperty("name").GetString() == "Accept-Language"),
                "STEP15-LANG-INTERNAL-EXEMPT-017 keeps internal operations language-neutral");
    }

    [Fact(DisplayName = "STEP15-SWAGGER-SCHEMA-NULLABILITY-139 STEP15-SWAGGER-SCHEMA-ENUMS-140 STEP15-SWAGGER-SCHEMA-BOUNDS-141 STEP15-SWAGGER-EXAMPLES-BUSINESS-147")]
    public async Task Business_DTOs_freeze_bilingual_selection_money_and_unknown_field_contracts()
    {
        using var document = await GetSwaggerAsync();
        var root = document.RootElement;

        foreach (var contract in new[]
                 {
                     new BilingualRequest("RegisterOwnerRequest", "companyNameAr", "companyNameHe"),
                     new BilingualRequest("CreateBranchRequest", "addressAr", "addressHe"),
                     new BilingualRequest("CreateServiceCategoryRequest", "nameAr", "nameHe"),
                     new BilingualRequest("CreateServiceOfferingRequest", "nameAr", "nameHe"),
                     new BilingualRequest("CreateAddonGroupRequest", "nameAr", "nameHe"),
                     new BilingualRequest("CreateAddonChoiceRequest", "nameAr", "nameHe")
                 })
        {
            var schema = Schema(root, contract.Schema);
            Required(schema).Should().Contain(contract.ArabicProperty,
                $"STEP15-SWAGGER-EXAMPLES-BUSINESS-147 requires Arabic in {contract.Schema}");
            var properties = schema.GetProperty("properties");
            Required(schema).Should().NotContain(contract.HebrewProperty);
            properties.GetProperty(contract.HebrewProperty).GetProperty("nullable")
                .GetBoolean().Should().BeTrue(
                    "STEP15-SWAGGER-SCHEMA-NULLABILITY-139 requires optional Hebrew nullable");
            schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
                "STEP15-SWAGGER-SCHEMA-BOUNDS-141 rejects unknown request fields");
        }

        var group = Schema(root, "CreateAddonGroupRequest");
        AssertIntegerBound(group, "minimumSelections", 0);
        AssertIntegerBound(group, "maximumSelections", 1);

        var choice = Schema(root, "CreateAddonChoiceRequest");
        AssertIntegerBound(choice, "minimumQuantity", 1);
        AssertIntegerBound(choice, "maximumQuantity", 1);

        var offering = Schema(root, "CreateServiceOfferingRequest");
        AssertDecimal(offering, "basePrice");
        AssertFormat(offering, "categoryId", "uuid");

        var appointment = Schema(root, "ValidateAppointmentRequest");
        AssertFormat(appointment, "branchId", "uuid");
        AssertFormat(appointment, "offeringId", "uuid");
        AssertFormat(appointment, "requestedSlotStartUtc", "date-time");
        var currency = appointment.GetProperty("properties").GetProperty("currency");
        currency.GetProperty("minLength").GetInt32().Should().Be(3);
        currency.GetProperty("maxLength").GetInt32().Should().Be(3);

        foreach (var enumSchema in Schemas(root).Where(schema =>
                     schema.Value.TryGetProperty("enum", out _)))
        {
            enumSchema.Value.GetProperty("type").GetString().Should().Be("string",
                $"STEP15-SWAGGER-SCHEMA-ENUMS-140 requires string enum {enumSchema.Name}");
        }

        offering.GetProperty("properties").EnumerateObject().Select(property => property.Name)
            .Should().NotContain(
                ["ownerUserId", "createdByUserId", "createdAtUtc", "updatedAtUtc"],
                "STEP15-SWAGGER-SCHEMA-BOUNDS-141 excludes server-owned fields");
    }

    [Fact(DisplayName = "STEP15-SWAGGER-PROBLEMS-136 STEP15-SWAGGER-CACHE-CORRELATION-149 STEP15-SWAGGER-EXAMPLES-LOCALIZED-142")]
    public async Task Business_problems_headers_and_localized_examples_are_complete()
    {
        using var document = await GetSwaggerAsync();
        var root = document.RootElement;
        var problem = Schema(root, "ProblemDetails");

        Required(problem).Should().BeEquivalentTo(
            ["type", "title", "status", "detail", "code", "correlationId"]);
        problem.GetProperty("properties").GetProperty("fieldErrors")
            .GetProperty("additionalProperties").GetProperty("items")
            .GetProperty("type").GetString().Should().Be("string");

        foreach (var operation in Operations(root))
        foreach (var response in operation.Value.GetProperty("responses").EnumerateObject()
                     .Where(response => int.TryParse(response.Name, out var status) && status >= 400))
        {
            response.Value.GetProperty("content").TryGetProperty(
                "application/problem+json", out _).Should().BeTrue(
                $"STEP15-SWAGGER-PROBLEMS-136 requires Problem Details for {operation.Method} {operation.Path}");
            response.Value.GetProperty("headers").TryGetProperty("X-Correlation-Id", out _)
                .Should().BeTrue("STEP15-SWAGGER-CACHE-CORRELATION-149");
            response.Value.GetProperty("headers").TryGetProperty("Cache-Control", out _)
                .Should().BeTrue("STEP15-SWAGGER-CACHE-CORRELATION-149");
        }

        var json = root.GetRawText();
        json.Should().Contain("تعذر إكمال الطلب.",
            "STEP15-SWAGGER-EXAMPLES-LOCALIZED-142 requires Arabic");
        json.Should().Contain("לא ניתן להשלים את הבקשה.",
            "STEP15-SWAGGER-EXAMPLES-LOCALIZED-142 requires Hebrew");
        json.Should().Contain("\"companyNameAr\"",
            "STEP15-SWAGGER-EXAMPLES-BUSINESS-147 requires Arabic company examples");
        json.Should().Contain("\"nameHe\": null",
            "STEP15-SWAGGER-EXAMPLES-BUSINESS-147 illustrates optional normalized Hebrew");
    }

    [Fact(DisplayName = "STEP15-SWAGGER-STATUS-BUSINESS-138")]
    public async Task Business_operations_declare_success_and_applicable_error_statuses()
    {
        using var document = await GetSwaggerAsync();
        var root = document.RootElement;

        AssertStatuses(root, "/api/v1/business/catalog/offerings", "post",
            ["201", "400", "401", "403", "404", "409", "413", "415", "500", "503"]);
        AssertStatuses(root, "/api/v1/business/availability/branches/{branchId}/settings", "put",
            ["200", "400", "401", "403", "404", "409", "413", "415", "500", "503"]);
        AssertStatuses(root, "/api/v1/business/catalog/categories/{categoryId}", "delete",
            ["204", "400", "401", "403", "404", "409", "500", "503"]);
        AssertStatuses(root, "/api/v1/internal/reservations", "post",
            ["200", "400", "401", "403", "409", "413", "415", "500"]);

        foreach (var operation in Operations(root))
        {
            operation.Value.GetProperty("responses").EnumerateObject()
                .Select(response => response.Name)
                .Should().NotContain("405",
                    $"STEP15-SWAGGER-STATUS-BUSINESS-138 excludes router-level 405 from {operation.Method} {operation.Path}");
        }
    }

    [Fact(DisplayName = "STEP15-SWAGGER-EXAMPLES-HMAC-146 STEP15-SWAGGER-EXAMPLE-REDACTION-150")]
    public async Task Business_HMAC_examples_are_safe_and_explain_canonical_replay_rules()
    {
        using var document = await GetSwaggerAsync();
        var json = document.RootElement.GetRawText();

        json.Should().Contain("<service-id>");
        json.Should().Contain("<utc-iso-timestamp>");
        json.Should().Contain("<unique-nonce>");
        json.Should().Contain("<hex-hmac-signature>");
        json.Should().Contain("canonical", "STEP15-SWAGGER-EXAMPLES-HMAC-146");
        json.Should().Contain("same-body replay", "STEP15-SWAGGER-IDEMPOTENCY-148");
        json.Should().NotMatchRegex(
            @"(?i)(sk_(live|test)_|Bearer\s+eyJ|@(?:gmail|outlook|hotmail)\.com)",
            "STEP15-SWAGGER-EXAMPLE-REDACTION-150 forbids secrets and PII");
    }

    [Fact(DisplayName = "STEP15-LANG-HEALTH-SWAGGER-EXEMPT-019 STEP15-HEADERS-NONLOCALIZED-166 STEP15-HEADERS-SWAGGER-CSP-170")]
    public async Task Business_swagger_json_is_language_neutral_no_store_and_hardened()
    {
        using var client = _factory.CreateSecureClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "he");
        using var response = await client.GetAsync("/swagger/v1/swagger.json?language=he");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.Contains("Content-Language").Should().BeFalse();
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        Header(response, "X-Content-Type-Options").Should().Equal("nosniff");
        Header(response, "X-Frame-Options").Should().Equal("DENY");
        Header(response, "Referrer-Policy").Should().Equal("no-referrer");
        Header(response, "Content-Security-Policy").Should().ContainSingle();
        Header(response, "Permissions-Policy").Should().ContainSingle();
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/swagger-ui.css")]
    [InlineData("/swagger/swagger-ui-bundle.js")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task SwaggerResources_UseNarrowSelfHostedUiCsp(string path)
    {
        using var client = _factory.CreateSecureClient();

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var csp = Header(response, "Content-Security-Policy").Should().ContainSingle().Subject;
        csp.Should().Contain("default-src 'self'");
        csp.Should().Contain("script-src 'self' 'unsafe-inline'");
        csp.Should().Contain("style-src 'self' 'unsafe-inline'");
        csp.Should().Contain("img-src 'self' data:");
        csp.Should().Contain("font-src 'self'");
        csp.Should().Contain("connect-src 'self'");
        csp.Should().Contain("object-src 'none'");
        csp.Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task ApiResources_KeepRestrictiveNonSwaggerCsp()
    {
        using var client = _factory.CreateSecureClient();

        using var response = await client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Header(response, "Content-Security-Policy").Should().Equal(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-SWAGGER-PRODUCTION-DISABLED-152")]
    public async Task Production_DisablesSwaggerAndRootUiRedirectWhileKeepingHsts()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Swagger:Enabled", "false");
        });
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var swagger = await client.GetAsync("/swagger/v1/swagger.json");
        using var root = await client.GetAsync("/");
        using var health = await client.GetAsync("/api/health");

        swagger.StatusCode.Should().Be(HttpStatusCode.NotFound);
        root.StatusCode.Should().Be(HttpStatusCode.NotFound);
        health.StatusCode.Should().Be(HttpStatusCode.OK);
        Header(health, "Strict-Transport-Security").Should().ContainSingle();
    }

    private async Task<JsonDocument> GetSwaggerAsync()
    {
        using var client = _factory.CreateSecureClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static readonly string[] BusinessOperations =
    [
        "POST /api/v1/business/auth/register-owner",
        "POST /api/v1/business/auth/login",
        "GET /api/v1/business/company",
        "PUT /api/v1/business/company",
        "POST /api/v1/business/company/branches",
        "PUT /api/v1/business/company/branches/{branchId}",
        "GET /api/v1/business/catalog/categories",
        "GET /api/v1/business/catalog/categories/{categoryId}",
        "POST /api/v1/business/catalog/categories",
        "PUT /api/v1/business/catalog/categories/{categoryId}",
        "DELETE /api/v1/business/catalog/categories/{categoryId}",
        "GET /api/v1/business/catalog/offerings",
        "GET /api/v1/business/catalog/offerings/{offeringId}",
        "POST /api/v1/business/catalog/offerings",
        "PUT /api/v1/business/catalog/offerings/{offeringId}",
        "DELETE /api/v1/business/catalog/offerings/{offeringId}",
        "GET /api/v1/business/catalog/offerings/{offeringId}/addon-groups",
        "GET /api/v1/business/catalog/addon-groups/{addonGroupId}",
        "POST /api/v1/business/catalog/offerings/{offeringId}/addon-groups",
        "PUT /api/v1/business/catalog/addon-groups/{addonGroupId}",
        "DELETE /api/v1/business/catalog/addon-groups/{addonGroupId}",
        "GET /api/v1/business/catalog/addon-groups/{addonGroupId}/choices",
        "GET /api/v1/business/catalog/addon-choices/{addonChoiceId}",
        "POST /api/v1/business/catalog/addon-groups/{addonGroupId}/choices",
        "PUT /api/v1/business/catalog/addon-choices/{addonChoiceId}",
        "DELETE /api/v1/business/catalog/addon-choices/{addonChoiceId}",
        "GET /api/v1/business/availability/branches/{branchId}/settings",
        "PUT /api/v1/business/availability/branches/{branchId}/settings",
        "GET /api/v1/business/availability/branches/{branchId}/recurring-schedules",
        "GET /api/v1/business/availability/recurring-schedules/{scheduleId}",
        "POST /api/v1/business/availability/branches/{branchId}/recurring-schedules",
        "PUT /api/v1/business/availability/recurring-schedules/{scheduleId}",
        "DELETE /api/v1/business/availability/recurring-schedules/{scheduleId}",
        "GET /api/v1/business/availability/branches/{branchId}/date-overrides",
        "GET /api/v1/business/availability/date-overrides/{overrideId}",
        "POST /api/v1/business/availability/branches/{branchId}/date-overrides",
        "PUT /api/v1/business/availability/date-overrides/{overrideId}",
        "DELETE /api/v1/business/availability/date-overrides/{overrideId}",
        "GET /api/v1/business/availability/branches/{branchId}/service-area",
        "PUT /api/v1/business/availability/branches/{branchId}/service-area",
        "DELETE /api/v1/business/availability/branches/{branchId}/service-area",
        "POST /api/v1/business/work-orders/{id}/transitions",
        "POST /api/v1/business/admin/booking-status-outbox/{eventId}/requeue",
        "GET /api/v1/internal/catalog/snapshot",
        "POST /api/v1/internal/appointments/validate",
        "POST /api/v1/internal/appointments/available-slots",
        "POST /api/v1/internal/reservations",
        "GET /api/v1/internal/reservations/{reference}",
        "GET /api/health"
    ];

    private static IEnumerable<Operation> Operations(JsonElement root)
    {
        foreach (var path in root.GetProperty("paths").EnumerateObject())
        foreach (var method in path.Value.EnumerateObject())
        {
            if (method.Name is "get" or "post" or "put" or "delete" or "patch")
                yield return new Operation(path.Name, method.Name.ToUpperInvariant(), method.Value);
        }
    }

    private static IEnumerable<JsonProperty> Schemas(JsonElement root) =>
        root.GetProperty("components").GetProperty("schemas").EnumerateObject();

    private static JsonElement Schema(JsonElement root, string suffix)
    {
        var match = Schemas(root).FirstOrDefault(schema =>
            schema.Name.Equals(suffix, StringComparison.OrdinalIgnoreCase));
        if (match.Name is null)
        {
            match = Schemas(root).SingleOrDefault(schema =>
                schema.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }
        match.Name.Should().NotBeNull($"schema *{suffix} must exist");
        return match.Value;
    }

    private static string[] Required(JsonElement schema) =>
        schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static JsonElement OperationAt(JsonElement root, string path, string method) =>
        root.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static void AssertHttpBearer(JsonElement root, string name)
    {
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty(name);
        scheme.GetProperty("type").GetString().Should().Be("http");
        scheme.GetProperty("scheme").GetString().Should().Be("bearer");
        scheme.GetProperty("bearerFormat").GetString().Should().Be("JWT");
    }

    private static void AssertApiKey(JsonElement root, string name, string header)
    {
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty(name);
        scheme.GetProperty("type").GetString().Should().Be("apiKey");
        scheme.GetProperty("in").GetString().Should().Be("header");
        scheme.GetProperty("name").GetString().Should().Be(header);
    }

    private static string[] SecurityNames(JsonElement operation)
    {
        if (!operation.TryGetProperty("security", out var security) ||
            security.ValueKind != JsonValueKind.Array ||
            security.GetArrayLength() == 0)
            return [];
        security.GetArrayLength().Should().Be(1,
            "the schemes in one requirement are ANDed; separate requirements would be ORed");
        return security[0].EnumerateObject().Select(item => item.Name).ToArray();
    }

    private static void AssertNoSecurity(JsonElement root, string path, string method)
    {
        var operation = OperationAt(root, path, method);
        operation.TryGetProperty("security", out var security).Should().BeTrue(
            $"STEP15-AUTH-ANONYMOUS-EXEMPTIONS-057 requires explicit exemption on {path}");
        security.GetArrayLength().Should().Be(0);
    }

    private static JsonElement AssertParameter(
        JsonElement operation, string name, string location, bool required)
    {
        var parameter = operation.GetProperty("parameters").EnumerateArray().Single(item =>
            item.GetProperty("name").GetString() == name &&
            item.GetProperty("in").GetString() == location);
        parameter.GetProperty("required").GetBoolean().Should().Be(required);
        return parameter;
    }

    private static void AssertHeader(
        JsonElement root,
        string path,
        string method,
        string name,
        bool required,
        int maxLength)
    {
        var parameter = AssertParameter(OperationAt(root, path, method), name, "header", required);
        parameter.GetProperty("schema").GetProperty("maxLength").GetInt32()
            .Should().Be(maxLength);
    }

    private static void AssertIntegerBound(JsonElement schema, string property, int minimum)
    {
        var value = schema.GetProperty("properties").GetProperty(property);
        value.GetProperty("type").GetString().Should().Be("integer");
        value.GetProperty("minimum").GetInt32().Should().Be(minimum);
    }

    private static void AssertDecimal(JsonElement schema, string property)
    {
        var value = schema.GetProperty("properties").GetProperty(property);
        value.GetProperty("type").GetString().Should().Be("number");
        value.GetProperty("format").GetString().Should().Be("decimal");
        value.GetProperty("multipleOf").GetDecimal().Should().Be(0.01m);
    }

    private static void AssertFormat(JsonElement schema, string property, string format) =>
        schema.GetProperty("properties").GetProperty(property).GetProperty("format")
            .GetString().Should().Be(format);

    private static void AssertStatuses(
        JsonElement root, string path, string method, string[] expected)
    {
        var responses = OperationAt(root, path, method).GetProperty("responses");
        responses.EnumerateObject().Select(response => response.Name)
            .Should().BeEquivalentTo(expected);
        foreach (var status in expected.Where(status => int.Parse(status) >= 400))
        {
            responses.GetProperty(status).GetProperty("content")
                .TryGetProperty("application/problem+json", out _).Should().BeTrue();
        }
    }

    private static string[] Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.ToArray() : [];

    private static void RequiredText(JsonElement value, string property, Operation operation)
    {
        value.TryGetProperty(property, out var text).Should().BeTrue(
            $"STEP15-SWAGGER-BUSINESS-ROUTES-131 requires {property} on {operation.Method} {operation.Path}");
        text.GetString().Should().NotBeNullOrWhiteSpace();
    }

    private readonly record struct Operation(string Path, string Method, JsonElement Value);
    private readonly record struct BilingualRequest(
        string Schema,
        string ArabicProperty,
        string HebrewProperty);
}
