using FluentAssertions;
using GhseeliApis.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text.Json;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Red contract tests for the frozen Step 15 Customer OpenAPI document.
/// </summary>
public sealed class Step15SwaggerCustomerContractTests :
    IClassFixture<Step15SwaggerCustomerFactory>
{
    private readonly Step15SwaggerCustomerFactory _factory;

    public Step15SwaggerCustomerContractTests(Step15SwaggerCustomerFactory factory) =>
        _factory = factory;

    [Fact(DisplayName = "STEP15-SWAGGER-CUSTOMER-ROUTES-130 STEP15-LEGACY-PAYMENTS-122")]
    public async Task Customer_document_has_exact_owned_operation_inventory()
    {
        using var document = await GetSwaggerAsync();
        var actual = Operations(document.RootElement)
            .Select(operation => $"{operation.Method} {operation.Path}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        actual.Should().Equal(CustomerOperations.Order(StringComparer.Ordinal),
            "the retained Step 15 Customer operations remain exactly discoverable");
        actual.Should().NotContain(operation =>
                operation.Contains("/api/v1/business/", StringComparison.OrdinalIgnoreCase),
            "STEP15-SWAGGER-CUSTOMER-ROUTES-130 excludes Business routes");
        actual.Should().NotContain(operation =>
                operation.StartsWith("POST /api/Payments", StringComparison.OrdinalIgnoreCase) ||
                operation.StartsWith("PUT /api/Payments", StringComparison.OrdinalIgnoreCase),
            "STEP15-LEGACY-PAYMENTS-122 excludes legacy payment writes");

        foreach (var operation in Operations(document.RootElement))
        {
            RequiredText(operation.Value, "operationId", operation);
            RequiredText(operation.Value, "summary", operation);
            RequiredText(operation.Value, "description", operation);
            operation.Value.GetProperty("tags").GetArrayLength().Should().BeGreaterThan(0,
                $"STEP15-SWAGGER-CUSTOMER-ROUTES-130 requires tags on {operation.Method} {operation.Path}");
        }
    }

    [Fact(DisplayName = "STEP15-SWAGGER-CUSTOMER-SECURITY-132 STEP15-SWAGGER-NO-GLOBAL-AUTH-134 STEP15-AUTH-ANONYMOUS-EXEMPTIONS-057")]
    public async Task Customer_security_schemes_and_per_operation_requirements_are_exact()
    {
        using var document = await GetSwaggerAsync();
        var root = document.RootElement;

        root.TryGetProperty("security", out _).Should().BeFalse(
            "STEP15-SWAGGER-NO-GLOBAL-AUTH-134 forbids global security");
        AssertHttpBearer(root, "CustomerBearer", "STEP15-SWAGGER-CUSTOMER-SECURITY-132");
        AssertApiKey(root, "DeviceToken", "X-Device-Token");
        AssertApiKey(root, "HmacServiceId", "X-Service-Id");
        AssertApiKey(root, "HmacTimestamp", "X-Timestamp");
        AssertApiKey(root, "HmacNonce", "X-Nonce");
        AssertApiKey(root, "HmacSignature", "X-Signature");
        AssertApiKey(root, "LahzaSignature", "X-Lahza-Signature");

        AssertNoSecurity(root, "/api/v1/devices/register", "post");
        AssertNoSecurity(root, "/api/Auth/register", "post");
        AssertNoSecurity(root, "/api/Auth/login", "post");
        AssertNoSecurity(root, "/api/Auth/external-login", "get");
        AssertNoSecurity(root, "/api/Auth/external-login-callback", "get");
        AssertNoSecurity(root, "/api/Health", "get");
        AssertNoSecurity(root, "/api/Health/db", "get");
        AssertSecurity(root, "/api/lahza/webhook", "post", ["LahzaSignature"]);

        AssertSecurity(root, "/api/v1/configuration", "get", ["DeviceToken"]);
        AssertSecurity(
            root,
            "/api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots",
            "post",
            ["DeviceToken"]);
        AssertSecurity(root, "/api/v1/checkout/drafts", "post", ["DeviceToken"]);
        AssertSecurity(root, "/api/v1/bookings/from-draft", "post",
            ["CustomerBearer", "DeviceToken"]);
        AssertSecurity(root, "/api/v1/payments/intents", "post",
            ["CustomerBearer", "DeviceToken"]);
        AssertSecurity(root, "/api/v1/payments/{id}", "get",
            ["CustomerBearer", "DeviceToken"]);

        foreach (var operation in Operations(root)
                     .Where(operation => operation.Path.StartsWith(
                         "/api/v1/internal/bookings", StringComparison.Ordinal)))
        {
            SecurityNames(operation.Value).Should().Equal(
                ["HmacServiceId", "HmacTimestamp", "HmacNonce", "HmacSignature"],
                $"STEP15-SWAGGER-CUSTOMER-SECURITY-132 requires the complete HMAC quartet on {operation.Method} {operation.Path}");
        }
    }

    [Fact(DisplayName = "STEP15-SWAGGER-IDEMPOTENCY-148 STEP15-INVARIANT-ORDERGUID-155 STEP15-SWAGGER-LANGUAGE-135")]
    public async Task Customer_operation_headers_are_placed_and_constrained()
    {
        using var document = await GetSwaggerAsync();
        var root = document.RootElement;

        AssertHeader(root, "/api/v1/bookings/from-draft", "post", "Idempotency-Key", true, 128);
        AssertHeader(root, "/api/v1/bookings/from-draft", "post", "X-Order-Guid", true, null, "uuid");
        AssertHeader(root, "/api/v1/payments/intents", "post", "Idempotency-Key", true, 128);
        AssertHeader(root, "/api/v1/internal/bookings/status", "post", "Idempotency-Key", true, 128);
        AssertHeader(root, "/api/v1/internal/bookings/{reference}/reconcile", "post", "Idempotency-Key", true, 128);

        foreach (var operation in Operations(root).Where(IsLocalizedCustomerOperation))
        {
            AssertParameter(operation.Value, "language", "query", required: false)
                .GetProperty("schema").GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()).Should().Equal(
                    ["ar", "he"],
                    $"STEP15-SWAGGER-LANGUAGE-135 requires exactly ar/he on {operation.Path}");
            AssertParameter(operation.Value, "Accept-Language", "header", required: false);
            AssertParameter(operation.Value, "X-Correlation-Id", "header", required: false);
        }
    }

    [Fact(DisplayName = "STEP15-SWAGGER-PROBLEMS-136 STEP15-PROBLEM-TYPE-CODE-033 STEP15-SWAGGER-CACHE-CORRELATION-149")]
    public async Task Customer_problem_contract_and_response_headers_are_reusable()
    {
        using var document = await GetSwaggerAsync();
        var root = document.RootElement;
        var problem = Schema(root, "ProblemDetails");

        problem.GetProperty("required").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo(
                ["type", "title", "status", "detail", "code", "correlationId"],
                "STEP15-SWAGGER-PROBLEMS-136 freezes the base envelope");
        problem.GetProperty("properties").GetProperty("type").GetProperty("format")
            .GetString().Should().Be("uri");
        problem.GetProperty("properties").GetProperty("fieldErrors")
            .GetProperty("additionalProperties").GetProperty("items")
            .GetProperty("type").GetString().Should().Be("string");

        foreach (var operation in Operations(root))
        {
            var responses = operation.Value.GetProperty("responses");
            foreach (var response in responses.EnumerateObject()
                         .Where(response => int.TryParse(response.Name, out var status) && status >= 400))
            {
                response.Value.GetProperty("content").TryGetProperty(
                    "application/problem+json", out _).Should().BeTrue(
                    $"STEP15-SWAGGER-PROBLEMS-136 requires problem media type for {operation.Method} {operation.Path} {response.Name}");
                response.Value.GetProperty("headers").TryGetProperty(
                    "X-Correlation-Id", out _).Should().BeTrue(
                    "STEP15-SWAGGER-CACHE-CORRELATION-149 documents correlation");
                response.Value.GetProperty("headers").TryGetProperty(
                    "Cache-Control", out _).Should().BeTrue(
                    "STEP15-SWAGGER-CACHE-CORRELATION-149 documents no-store");
            }
        }
    }

    [Fact(DisplayName = "STEP15-SWAGGER-SCHEMA-NULLABILITY-139 STEP15-SWAGGER-SCHEMA-ENUMS-140 STEP15-SWAGGER-SCHEMA-BOUNDS-141 STEP15-INVARIANT-SERIALIZATION-164")]
    public async Task Customer_DTO_schemas_freeze_binding_money_enum_and_ownership_rules()
    {
        using var document = await GetSwaggerAsync();
        var root = document.RootElement;

        var paymentRequest = Schema(root, "CreateCustomerPaymentIntentRequest");
        Required(paymentRequest).Should().BeEquivalentTo(["bookingId", "method"]);
        paymentRequest.GetProperty("properties").EnumerateObject().Select(x => x.Name)
            .Should().BeEquivalentTo(["bookingId", "method"],
                "STEP15-SWAGGER-EXAMPLES-PAYMENT-145 excludes client money/provider fields");
        AssertFormat(paymentRequest, "bookingId", "uuid");
        AssertStringEnum(paymentRequest, "method", ["Card", "Wallet", "CashOnArrival", "ThirdParty"]);

        var selections = Schemas(root).Where(schema =>
            schema.Name.Contains("Selection", StringComparison.OrdinalIgnoreCase) &&
            HasProperty(schema.Value, "quantity")).ToArray();
        selections.Should().NotBeEmpty(
            "STEP15-INVARIANT-SELECTION-156 requires a documented selection DTO");
        var selection = selections[0];
        selection.Value.GetProperty("properties").GetProperty("quantity")
            .GetProperty("minimum").GetInt32().Should().Be(1);

        foreach (var schema in Schemas(root)
                     .Where(schema => schema.Value.TryGetProperty("properties", out var properties) &&
                                      properties.TryGetProperty("currency", out _)))
        {
            var currency = schema.Value.GetProperty("properties").GetProperty("currency");
            currency.GetProperty("type").GetString().Should().Be("string");
            currency.GetProperty("minLength").GetInt32().Should().Be(3);
            currency.GetProperty("maxLength").GetInt32().Should().Be(3);
        }

        paymentRequest.GetProperty("properties").EnumerateObject()
            .Select(property => property.Name)
            .Should().NotContain(
                ["userId", "deviceId", "amount", "currency", "providerSecret", "clientSecret"],
                "STEP15-INVARIANT-PAYMENT-159 excludes ownership, money, and provider-managed inputs");
    }

    [Fact(DisplayName = "STEP15-SWAGGER-EXAMPLES-LOCALIZED-142 STEP15-SWAGGER-EXAMPLES-CHECKOUT-143 STEP15-SWAGGER-EXAMPLES-BOOKING-144 STEP15-SWAGGER-EXAMPLES-PAYMENT-145 STEP15-SWAGGER-EXAMPLES-HMAC-146")]
    public async Task Customer_examples_cover_localization_checkout_booking_payment_and_HMAC()
    {
        using var document = await GetSwaggerAsync();
        var json = document.RootElement.GetRawText();

        json.Should().Contain("تعذر إكمال الطلب.",
            "STEP15-SWAGGER-EXAMPLES-LOCALIZED-142 requires Arabic examples");
        json.Should().Contain("לא ניתן להשלים את הבקשה.",
            "STEP15-SWAGGER-EXAMPLES-LOCALIZED-142 requires Hebrew examples");
        json.Should().Contain("\"orderGuid\"",
            "STEP15-SWAGGER-EXAMPLES-CHECKOUT-143 requires checkout order identity");
        json.Should().Contain("\"grandTotal\"",
            "STEP15-SWAGGER-EXAMPLES-CHECKOUT-143 requires authoritative totals");
        json.Should().Contain("same-body replay",
            "STEP15-SWAGGER-EXAMPLES-BOOKING-144 requires replay semantics");
        json.Should().Contain("server-authoritative",
            "STEP15-SWAGGER-EXAMPLES-PAYMENT-145 requires server money semantics");
        json.Should().Contain("<service-id>",
            "STEP15-SWAGGER-EXAMPLES-HMAC-146 requires safe placeholders");
        json.Should().NotMatchRegex(
            @"(?i)(sk_(live|test)_|Bearer\s+eyJ|X-Lahza-Signature[""']?\s*[:=]\s*[""']?t=\d)",
            "STEP15-SWAGGER-EXAMPLE-REDACTION-150 forbids credential-like examples");
    }

    [Fact(DisplayName = "STEP15-SWAGGER-STATUS-CUSTOMER-137 STEP15-SWAGGER-PROBLEMS-136")]
    public async Task Customer_operations_declare_runtime_status_and_media_contracts()
    {
        using var document = await GetSwaggerAsync();
        var root = document.RootElement;

        AssertStatuses(root, "/api/v1/bookings/from-draft", "post",
            ["200", "400", "401", "403", "404", "405", "409", "410", "413", "415", "500", "502", "503"]);
        AssertStatuses(root, "/api/v1/payments/intents", "post",
            ["200", "400", "401", "403", "404", "405", "409", "413", "415", "500", "502", "503"]);
        AssertStatuses(root, "/api/v1/internal/bookings/status", "post",
            ["200", "400", "401", "405", "409", "413", "415", "500", "503"]);
        AssertStatuses(root, "/api/lahza/webhook", "post",
            ["200", "400", "401", "405", "413", "415", "500", "503"]);

        var catalogGet = OperationAt(root, "/api/v1/catalog/businesses", "get")
            .GetProperty("responses").EnumerateObject().Select(response => response.Name);
        catalogGet.Should().NotContain(["403", "409", "410", "413", "415", "502"],
            "STEP15-SWAGGER-STATUS-CUSTOMER-137 forbids blanket impossible GET errors");

        var healthGet = OperationAt(root, "/api/Health", "get")
            .GetProperty("responses").EnumerateObject().Select(response => response.Name);
        healthGet.Should().NotContain(["400", "401", "403", "404", "409", "413", "415", "503"],
            "STEP15-SWAGGER-STATUS-CUSTOMER-137 keeps health's contract operation-specific");
    }

    [Fact(DisplayName = "STEP15-SWAGGER-REFS-138 STEP15-SWAGGER-DETERMINISTIC-151")]
    public async Task Customer_document_has_resolved_request_refs_unique_operationIds_and_stable_bytes()
    {
        using var client = _factory.CreateApiClient();
        var first = await client.GetByteArrayAsync("/swagger/v1/swagger.json");
        var second = await client.GetByteArrayAsync("/swagger/v1/swagger.json");
        first.Should().Equal(second,
            "STEP15-SWAGGER-DETERMINISTIC-151 requires repeated generation to be byte stable");

        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        var operationIds = Operations(root)
            .Select(operation => operation.Value.GetProperty("operationId").GetString()!)
            .ToArray();
        operationIds.Should().OnlyHaveUniqueItems();

        var paymentRequest = OperationAt(root, "/api/v1/payments/intents", "post")
            .GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema")
            .GetProperty("$ref").GetString();
        paymentRequest.Should().Be(
            "#/components/schemas/CreateCustomerPaymentIntentRequest");

        foreach (var operation in Operations(root))
        foreach (var response in operation.Value.GetProperty("responses").EnumerateObject()
                     .Where(response => int.TryParse(response.Name, out var status) && status >= 400))
        {
            response.Value.GetProperty("content")
                .GetProperty("application/problem+json")
                .GetProperty("schema")
                .GetProperty("$ref").GetString()
                .Should().Be("#/components/schemas/ProblemDetails");
        }
    }

    [Fact(DisplayName = "STEP15-LEGACY-AUTH-114 STEP15-LEGACY-USERS-115 STEP15-LEGACY-ADDRESSES-116 STEP15-LEGACY-VEHICLES-117 STEP15-LEGACY-HEALTH-123")]
    public async Task Retained_legacy_operations_have_exact_ownership_security_and_transport_shapes()
    {
        using var document = await GetSwaggerAsync();
        var root = document.RootElement;

        AssertNoSecurity(root, "/api/Auth/register", "post");
        AssertNoSecurity(root, "/api/Auth/login", "post");
        foreach (var path in new[]
                 {
                     "/api/Auth/me",
                     "/api/Users",
                     "/api/Addresses/my-addresses",
                     "/api/Vehicles/my-vehicles"
                 })
        {
            AssertSecurity(root, path, "get", ["CustomerBearer"]);
            var responses = OperationAt(root, path, "get").GetProperty("responses")
                .EnumerateObject().Select(response => response.Name).ToArray();
            responses.Should().Contain(["200", "400", "401", "403", "405", "500"]);
            responses.Should().NotContain(["409", "410", "413", "415", "502", "503"],
                $"{path} is a bodyless legacy read operation");
        }

        AssertNoSecurity(root, "/api/Health", "get");
        AssertNoSecurity(root, "/api/Health/db", "get");
        var health = OperationAt(root, "/api/Health", "get");
        (!health.TryGetProperty("parameters", out var parameters) ||
         parameters.GetArrayLength() == 0).Should().BeTrue();
    }

    [Fact(DisplayName = "STEP15-LANG-HEALTH-SWAGGER-EXEMPT-019 STEP15-HEADERS-NONLOCALIZED-166 STEP15-HEADERS-SWAGGER-CSP-170")]
    public async Task Customer_swagger_json_is_language_neutral_no_store_and_hardened()
    {
        using var client = _factory.CreateApiClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "he");
        using var response = await client.GetAsync("/swagger/v1/swagger.json?language=he");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.Contains("Content-Language").Should().BeFalse(
            "STEP15-LANG-HEALTH-SWAGGER-EXEMPT-019 exempts Swagger localization");
        response.Headers.CacheControl?.NoStore.Should().BeTrue(
            "STEP15-HEADERS-SWAGGER-CSP-170 requires no-store");
        Header(response, "X-Content-Type-Options").Should().Equal("nosniff");
        Header(response, "X-Frame-Options").Should().Equal("DENY");
        Header(response, "Referrer-Policy").Should().Equal("no-referrer");
        Header(response, "Content-Security-Policy").Should().ContainSingle();
        Header(response, "Permissions-Policy").Should().ContainSingle();
    }

    private async Task<JsonDocument> GetSwaggerAsync()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static bool IsLocalizedCustomerOperation(Operation operation) =>
        operation.Path.StartsWith("/api/v1/", StringComparison.Ordinal) &&
        !operation.Path.StartsWith("/api/v1/internal/", StringComparison.Ordinal) &&
        operation.Path != "/api/v1/devices/register";

    private static readonly string[] CustomerOperations =
    [
        "POST /api/v1/devices/register",
        "GET /api/v1/configuration",
        "GET /api/v1/catalog/categories",
        "GET /api/v1/catalog/businesses",
        "GET /api/v1/catalog/businesses/{id}",
        "GET /api/v1/catalog/businesses/{id}/offerings",
        "POST /api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots",
        "GET /api/v1/catalog/offerings/{id}",
        "POST /api/v1/checkout/drafts",
        "GET /api/v1/checkout/drafts/{orderGuid}",
        "PUT /api/v1/checkout/drafts/{orderGuid}",
        "POST /api/v1/pricing/reprice",
        "POST /api/v1/checkout/reprice",
        "POST /api/v1/bookings/from-draft",
        "POST /api/v1/payments/intents",
        "GET /api/v1/payments/{id}",
        "POST /api/v1/payments/{id}/verify",
        "POST /api/v1/internal/bookings/status",
        "POST /api/v1/internal/bookings/{reference}/reconcile",
        "GET /api/v1/internal/bookings/{reference}",
        "POST /api/lahza/webhook",
        "POST /api/Auth/register",
        "POST /api/Auth/login",
        "POST /api/Auth/validate",
        "GET /api/Auth/me",
        "GET /api/Auth/external-login",
        "GET /api/Auth/external-login-callback",
        "POST /api/Auth/link-external-login",
        "GET /api/Auth/link-external-login-callback",
        "DELETE /api/Auth/external-login/{provider}",
        "GET /api/Auth/external-logins",
        "GET /api/Users",
        "GET /api/Users/{id}",
        "POST /api/Users",
        "PUT /api/Users/{id}",
        "DELETE /api/Users/{id}",
        "GET /api/Users/me",
        "PUT /api/Users/me",
        "DELETE /api/Users/me",
        "PUT /api/Users/me/reactivate",
        "PUT /api/Users/me/password",
        "POST /api/Users/me/email/request-confirmation",
        "POST /api/Users/me/email/confirm",
        "GET /api/Addresses/my-addresses",
        "GET /api/Addresses/{id}",
        "POST /api/Addresses",
        "PUT /api/Addresses/{id}",
        "DELETE /api/Addresses/{id}",
        "PUT /api/Addresses/{id}/set-primary",
        "GET /api/Vehicles/my-vehicles",
        "GET /api/Vehicles/{id}",
        "POST /api/Vehicles",
        "PUT /api/Vehicles/{id}",
        "DELETE /api/Vehicles/{id}",
        "GET /api/Health",
        "GET /api/Health/db"
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
        var match = Schemas(root).SingleOrDefault(schema =>
            schema.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        match.Name.Should().NotBeNull($"schema *{suffix} must exist");
        return match.Value;
    }

    private static bool HasProperty(JsonElement schema, string property) =>
        schema.TryGetProperty("properties", out var properties) &&
        properties.TryGetProperty(property, out _);

    private static string[] Required(JsonElement schema) =>
        schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static void AssertFormat(JsonElement schema, string property, string format) =>
        schema.GetProperty("properties").GetProperty(property).GetProperty("format")
            .GetString().Should().Be(format);

    private static void AssertStringEnum(JsonElement schema, string property, string[] values)
    {
        var value = schema.GetProperty("properties").GetProperty(property);
        value.GetProperty("type").GetString().Should().Be("string");
        value.GetProperty("enum").EnumerateArray().Select(item => item.GetString())
            .Should().Equal(values);
    }

    private static JsonElement OperationAt(JsonElement root, string path, string method) =>
        root.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static void AssertHttpBearer(JsonElement root, string name, string scenario)
    {
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty(name);
        scheme.GetProperty("type").GetString().Should().Be("http", scenario);
        scheme.GetProperty("scheme").GetString().Should().Be("bearer", scenario);
        scheme.GetProperty("bearerFormat").GetString().Should().Be("JWT", scenario);
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
            "combined requirements must be one AND requirement, not OR alternatives");
        return security[0].EnumerateObject().Select(item => item.Name).ToArray();
    }

    private static void AssertSecurity(
        JsonElement root, string path, string method, string[] expected) =>
        SecurityNames(OperationAt(root, path, method)).Should().Equal(expected,
            $"STEP15 security requirement for {method.ToUpperInvariant()} {path}");

    private static void AssertNoSecurity(JsonElement root, string path, string method)
    {
        var operation = OperationAt(root, path, method);
        operation.TryGetProperty("security", out var security).Should().BeTrue(
            $"STEP15-AUTH-ANONYMOUS-EXEMPTIONS-057 requires explicit anonymous security for {path}");
        security.ValueKind.Should().Be(JsonValueKind.Array);
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
        int? maxLength,
        string? format = null)
    {
        var parameter = AssertParameter(OperationAt(root, path, method), name, "header", required);
        var schema = parameter.GetProperty("schema");
        if (maxLength.HasValue)
            schema.GetProperty("maxLength").GetInt32().Should().Be(maxLength.Value);
        if (format is not null)
            schema.GetProperty("format").GetString().Should().Be(format);
    }

    private static void AssertStatuses(
        JsonElement root, string path, string method, string[] expected)
    {
        var actual = OperationAt(root, path, method).GetProperty("responses")
            .EnumerateObject().Select(response => response.Name).ToArray();
        actual.Order(StringComparer.Ordinal).Should().Equal(
            expected.Order(StringComparer.Ordinal),
            $"STEP15 status contract for {method.ToUpperInvariant()} {path}");
        foreach (var status in expected.Where(status => int.Parse(status) >= 400))
        {
            OperationAt(root, path, method).GetProperty("responses").GetProperty(status)
                .GetProperty("content").TryGetProperty("application/problem+json", out _)
                .Should().BeTrue();
        }
    }

    private static string[] Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.ToArray() : [];

    private static void RequiredText(JsonElement value, string property, Operation operation)
    {
        value.TryGetProperty(property, out var text).Should().BeTrue(
            $"STEP15-SWAGGER-CUSTOMER-ROUTES-130 requires {property} on {operation.Method} {operation.Path}");
        text.GetString().Should().NotBeNullOrWhiteSpace();
    }

    private readonly record struct Operation(string Path, string Method, JsonElement Value);
}

public sealed class Step15SwaggerCustomerFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"Step15SwaggerCustomer-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:CustomerConnection", "Step15SwaggerCustomer");
        builder.UseSetting("JwtSettings:SecretKey", "Step15SwaggerCustomerSecret_Minimum32Chars");
        builder.UseSetting("JwtSettings:Issuer", "GhseeliApis.Step15SwaggerTests");
        builder.UseSetting("JwtSettings:Audience", "GhseeliApis.Step15SwaggerClients");
        builder.UseSetting("Swagger:Enabled", "true");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
}
