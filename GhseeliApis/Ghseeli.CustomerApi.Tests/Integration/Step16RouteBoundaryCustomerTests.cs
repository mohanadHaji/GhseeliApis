using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Business;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Step 16 red contracts for the Customer host's exact route responsibility boundary.
/// </summary>
public sealed class Step16RouteBoundaryCustomerTests : IAsyncLifetime
{
    private static readonly Guid Id = Guid.Parse("16161616-1616-1616-1616-161616161616");
    private readonly CountingBusinessApiClient _outbound = new();
    private readonly Step15CustomerApiFactory _factory;

    public Step16RouteBoundaryCustomerTests()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Id, version: 16);
        _factory = new Step15CustomerApiFactory(
            snapshot,
            [CatalogTestSupport.CreateDevice(
                Step15CustomerFixture.DeviceToken,
                DateTimeOffset.UtcNow.AddDays(1))],
            services =>
            {
                services.RemoveAll<IBusinessApiClient>();
                services.AddSingleton<IBusinessApiClient>(_outbound);
            });
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Theory(DisplayName = "STEP16-FOREIGN-HEALTH-NAMES-135")]
    [InlineData("/api/health")]
    [InlineData("/api/HEALTH")]
    [InlineData("/API/HEALTH")]
    [InlineData("/Api/health")]
    [InlineData("/api/HeAlTh/")]
    [InlineData("/api/health/db")]
    public async Task NoncanonicalBusinessHealthAliases_ReturnRouteLevelNotFound(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public static TheoryData<string, string, string> RemovedLegacyOperations => new()
    {
        { "STEP16-REMOVED-COMPANIES-READS-113", "GET", "/api/Companies" },
        { "STEP16-REMOVED-COMPANIES-READS-113", "GET", $"/api/Companies/{Id:D}" },
        { "STEP16-REMOVED-COMPANIES-READS-113", "GET", "/api/Companies/area/Haifa" },
        { "STEP16-REMOVED-COMPANIES-WRITES-114", "POST", "/api/Companies/create" },
        { "STEP16-REMOVED-COMPANIES-WRITES-114", "PUT", $"/api/Companies/{Id:D}" },
        { "STEP16-REMOVED-COMPANIES-WRITES-114", "DELETE", $"/api/Companies/{Id:D}" },
        { "STEP16-REMOVED-SERVICES-READS-115", "GET", "/api/Services" },
        { "STEP16-REMOVED-SERVICES-READS-115", "GET", $"/api/Services/{Id:D}" },
        { "STEP16-REMOVED-SERVICES-READS-115", "GET", $"/api/Services/{Id:D}/with-options" },
        { "STEP16-REMOVED-SERVICES-WRITES-116", "POST", "/api/Services" },
        { "STEP16-REMOVED-SERVICES-WRITES-116", "PUT", $"/api/Services/{Id:D}" },
        { "STEP16-REMOVED-SERVICES-WRITES-116", "DELETE", $"/api/Services/{Id:D}" },
        { "STEP16-REMOVED-OPTIONS-READS-117", "GET", "/api/ServiceOptions" },
        { "STEP16-REMOVED-OPTIONS-READS-117", "GET", $"/api/ServiceOptions/{Id:D}" },
        { "STEP16-REMOVED-OPTIONS-READS-117", "GET", $"/api/ServiceOptions/service/{Id:D}" },
        { "STEP16-REMOVED-OPTIONS-READS-117", "GET", $"/api/ServiceOptions/company/{Id:D}" },
        { "STEP16-REMOVED-OPTIONS-WRITES-118", "POST", "/api/ServiceOptions" },
        { "STEP16-REMOVED-OPTIONS-WRITES-118", "PUT", $"/api/ServiceOptions/{Id:D}" },
        { "STEP16-REMOVED-OPTIONS-WRITES-118", "DELETE", $"/api/ServiceOptions/{Id:D}" },
        { "STEP16-REMOVED-BOOKINGS-CUSTOMER-119", "GET", "/api/Bookings/my-bookings" },
        { "STEP16-REMOVED-BOOKINGS-CUSTOMER-119", "GET", "/api/Bookings/my-bookings/upcoming" },
        { "STEP16-REMOVED-BOOKINGS-CUSTOMER-119", "GET", "/api/Bookings/my-bookings/history" },
        { "STEP16-REMOVED-BOOKINGS-BUSINESS-120", "GET", $"/api/Bookings/company/{Id:D}" },
        { "STEP16-REMOVED-BOOKINGS-CUSTOMER-119", "GET", $"/api/Bookings/{Id:D}" },
        { "STEP16-REMOVED-BOOKINGS-BUSINESS-120", "POST", "/api/Bookings" },
        { "STEP16-REMOVED-BOOKINGS-BUSINESS-120", "PUT", $"/api/Bookings/{Id:D}" },
        { "STEP16-REMOVED-BOOKINGS-CUSTOMER-119", "PUT", $"/api/Bookings/{Id:D}/cancel" },
        { "STEP16-REMOVED-BOOKINGS-BUSINESS-120", "PUT", $"/api/Bookings/{Id:D}/confirm" },
        { "STEP16-REMOVED-BOOKINGS-BUSINESS-120", "PUT", $"/api/Bookings/{Id:D}/start" },
        { "STEP16-REMOVED-BOOKINGS-BUSINESS-120", "PUT", $"/api/Bookings/{Id:D}/complete" },
        { "STEP16-REMOVED-BOOKINGS-CUSTOMER-119", "GET", "/api/Bookings/check-availability" },
        { "STEP16-REMOVED-PAYMENTS-121", "GET", "/api/Payments" },
        { "STEP16-REMOVED-PAYMENTS-121", "GET", $"/api/Payments/{Id:D}" },
        { "STEP16-REMOVED-PAYMENTS-121", "GET", "/api/Payments/my-payments" },
        { "STEP16-REMOVED-PAYMENTS-121", "GET", $"/api/Payments/booking/{Id:D}" },
        { "STEP16-REMOVED-PAYMENTS-121", "POST", "/api/Payments" },
        { "STEP16-REMOVED-PAYMENTS-121", "PUT", $"/api/Payments/{Id:D}/status" },
        { "STEP16-REMOVED-PAYMENTS-121", "POST", $"/api/Payments/{Id:D}/refund" }
    };

    [Theory]
    [MemberData(nameof(RemovedLegacyOperations))]
    public async Task RemovedLegacyOperation_Is404BeforeEveryFormerCredential_AndHasNoSideEffects(
        string scenarioId,
        string method,
        string path)
    {
        var before = StateSnapshot();
        var outboundBefore = _outbound.TotalCalls;

        foreach (var role in new string?[] { null, "User", "Company", "Admin" })
        {
            using var client = _factory.CreateApiClient();
            using var request = Request(method, path, role);
            using var response = await client.SendAsync(request);
            await AssertRouteLevel404Async(response, scenarioId, method, path);
        }

        StateSnapshot().Should().Equal(before,
            $"{scenarioId} forbids DbContext, Identity, payment, version, or persistence effects");
        _outbound.TotalCalls.Should().Be(outboundBefore,
            $"{scenarioId} forbids Business/provider/payment outbound work");
    }

    [Fact(DisplayName = "STEP16-ROUTES-CUSTOMER-EXACT-065 STEP16-REMOVED-COMPANIES-READS-113 STEP16-REMOVED-SERVICES-READS-115 STEP16-REMOVED-OPTIONS-READS-117 STEP16-REMOVED-BOOKINGS-CUSTOMER-119 STEP16-REMOVED-PAYMENTS-121")]
    public void ControllerActionDiscovery_ContainsOnlyRetainedCustomerControllerOperations()
    {
        var provider = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var actual = provider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .SelectMany(action => action.ActionConstraints!
                .OfType<HttpMethodActionConstraint>()
                .SelectMany(constraint => constraint.HttpMethods.Select(method =>
                    $"{method.ToUpperInvariant()} /{NormalizeTemplate(action.AttributeRouteInfo!.Template!)}")))
            .Order(StringComparer.Ordinal)
            .ToArray();

        actual.Should().Equal(CustomerRuntimeOperations.Order(StringComparer.Ordinal),
            "STEP16-ROUTES-CUSTOMER-EXACT-065 forbids legacy controller/action discovery");
    }

    [Fact(DisplayName = "STEP16-SWAGGER-CUSTOMER-EXACT-067 STEP16-CUSTOMER-REGRESSION-096")]
    public async Task OpenApi_ContainsEveryRetainedCustomerOperation_WithExactSecurity_AndNoLegacySchemas()
    {
        using var document = await SwaggerAsync();
        var operations = Operations(document.RootElement).ToArray();

        operations.Select(x => $"{x.Method} {x.Path}").Order(StringComparer.Ordinal)
            .Should().Equal(CustomerOpenApiOperations.Order(StringComparer.Ordinal));
        foreach (var operation in operations)
        {
            SecurityNames(operation.Value).Should().Equal(
                ExpectedCustomerSecurity(operation.Method, operation.Path),
                $"STEP16-SWAGGER-CUSTOMER-EXACT-067 security for {operation.Method} {operation.Path}");
        }

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Select(x => x.Name).ToArray();
        schemas.Should().NotContain(LegacySchemaNames,
            "STEP16 removed controllers must not keep request/response schemas discoverable");
    }

    [Theory]
    [MemberData(nameof(BusinessWrongHostOperations))]
    public async Task EveryBusinessOperation_OnCustomerHost_IsPreAuth404WithNoSideEffects(
        string method,
        string path)
    {
        var before = StateSnapshot();
        var outboundBefore = _outbound.TotalCalls;
        using var client = _factory.CreateApiClient();
        using var request = Request(method, path, "Owner");
        request.Headers.TryAddWithoutValidation("X-Ghseeli-Service-Id", "business");
        request.Headers.TryAddWithoutValidation("X-Ghseeli-Signature", "not-consumed");

        using var response = await client.SendAsync(request);

        await AssertRouteLevel404Async(
            response, "STEP16-REMOVED-BUSINESS-ALIASES-123/124", method, path);
        StateSnapshot().Should().Equal(before);
        _outbound.TotalCalls.Should().Be(outboundBefore);
    }

    [Fact(DisplayName = "STEP16-HEALTH-CUSTOMER-060 STEP16-CUSTOMER-REGRESSION-096")]
    public async Task TinyRetainedCustomerRoutes_StayDiscoverable_AndHealthGetHeadDbRemainCallable()
    {
        using var client = _factory.CreateApiClient();
        foreach (var (method, path) in new[]
                 {
                     ("GET", "/api/Health"),
                     ("HEAD", "/api/Health"),
                     ("GET", "/api/Health/db")
                 })
        {
            using var response = await client.SendAsync(new HttpRequestMessage(
                new HttpMethod(method), path));
            response.StatusCode.Should().NotBe(HttpStatusCode.NotFound, $"{method} {path} is retained");
        }

        CustomerOpenApiOperations.Should().Contain(
            [
                "GET /api/Auth/external-login-callback",
                "GET /api/Auth/link-external-login-callback",
                "POST /api/Users/me/email/request-confirmation",
                "POST /api/Users/me/email/confirm",
                "PUT /api/Addresses/{id}/set-primary",
                "GET /api/Health/db"
            ]);
    }

    public static TheoryData<string, string> BusinessWrongHostOperations =>
        ToTheoryData(BusinessOperations);

    private static readonly string[] CustomerOpenApiOperations =
    [
        "POST /api/Auth/register", "POST /api/Auth/login", "POST /api/Auth/validate",
        "GET /api/Auth/me", "GET /api/Auth/external-login",
        "GET /api/Auth/external-login-callback", "POST /api/Auth/link-external-login",
        "GET /api/Auth/link-external-login-callback",
        "DELETE /api/Auth/external-login/{provider}", "GET /api/Auth/external-logins",
        "GET /api/Users", "GET /api/Users/{id}", "POST /api/Users",
        "PUT /api/Users/{id}", "DELETE /api/Users/{id}", "GET /api/Users/me",
        "PUT /api/Users/me", "DELETE /api/Users/me", "PUT /api/Users/me/reactivate",
        "PUT /api/Users/me/password", "POST /api/Users/me/email/request-confirmation",
        "POST /api/Users/me/email/confirm", "GET /api/Addresses/my-addresses",
        "GET /api/Addresses/{id}", "POST /api/Addresses", "PUT /api/Addresses/{id}",
        "DELETE /api/Addresses/{id}", "PUT /api/Addresses/{id}/set-primary",
        "GET /api/Vehicles/my-vehicles", "GET /api/Vehicles/{id}", "POST /api/Vehicles",
        "PUT /api/Vehicles/{id}", "DELETE /api/Vehicles/{id}",
        "POST /api/v1/devices/register", "GET /api/v1/configuration",
        "GET /api/v1/catalog/categories", "GET /api/v1/catalog/businesses",
        "GET /api/v1/catalog/businesses/{id}",
        "GET /api/v1/catalog/businesses/{id}/offerings",
        "POST /api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots",
        "GET /api/v1/catalog/offerings/{id}", "POST /api/v1/checkout/drafts",
        "GET /api/v1/checkout/drafts/{orderGuid}",
        "PUT /api/v1/checkout/drafts/{orderGuid}", "POST /api/v1/pricing/reprice",
        "POST /api/v1/checkout/reprice", "POST /api/v1/bookings/from-draft",
        "POST /api/v1/payments/intents", "GET /api/v1/payments/{id}",
        "POST /api/v1/payments/{id}/verify",
        "POST /api/v1/internal/bookings/status",
        "POST /api/v1/internal/bookings/{reference}/reconcile",
        "GET /api/v1/internal/bookings/{reference}", "POST /api/lahza/webhook",
        "GET /api/Health", "GET /api/Health/db"
    ];

    private static readonly string[] CustomerRuntimeOperations =
        [.. CustomerOpenApiOperations, "POST /api/v1/internal/bookings/{unmatched}"];

    private static readonly string[] BusinessOperations =
    [
        "POST /api/v1/business/auth/register-owner", "POST /api/v1/business/auth/login",
        "GET /api/v1/business/company", "PUT /api/v1/business/company",
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
        "POST /api/v1/internal/appointments/validate", "POST /api/v1/internal/reservations",
        "GET /api/v1/internal/reservations/{reference}", "GET /api/health"
    ];

    private static readonly string[] LegacySchemaNames =
    [
        "CreateCompanyRequest", "UpdateCompanyRequest", "CompanyResponse", "CompanyListResponse",
        "CreateServiceRequest", "UpdateServiceRequest", "ServiceResponse",
        "CreateServiceOptionRequest", "UpdateServiceOptionRequest", "ServiceOptionResponse",
        "CreateBookingRequest", "UpdateBookingRequest", "BookingResponse",
        "CreatePaymentRequest", "UpdatePaymentStatusRequest", "PaymentResponse"
    ];

    private HttpRequestMessage Request(string method, string path, string? role)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), Expand(path));
        if (method is "POST" or "PUT" or "PATCH")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        if (role is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", Step15CustomerAssertions.Jwt(role));
        return request;
    }

    private static string Expand(string path) => path
        .Replace("{id}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{branchId}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{categoryId}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{offeringId}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{addonGroupId}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{addonChoiceId}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{scheduleId}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{overrideId}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{eventId}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{reference}", Id.ToString("D"), StringComparison.Ordinal);

    private static string NormalizeTemplate(string template) =>
        template.Replace(":guid}", "}", StringComparison.Ordinal);

    private string[] StateSnapshot()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return context.Model.GetEntityTypes()
            .Where(x => x.ClrType is not null)
            .Select(x => $"{x.Name}={Count(context, x.ClrType)}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static int Count(DbContext context, Type entityType)
    {
        var set = typeof(DbContext).GetMethods().Single(x =>
                x.Name == nameof(DbContext.Set) && x.IsGenericMethod &&
                x.GetParameters().Length == 0)
            .MakeGenericMethod(entityType).Invoke(context, null);
        return ((IEnumerable)set!).Cast<object>().Count();
    }

    private async Task<JsonDocument> SwaggerAsync()
    {
        using var client = _factory.CreateApiClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertRouteLevel404Async(
        HttpResponseMessage response, string scenarioId, string method, string path)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"{scenarioId} requires {method} {path} to miss routing before auth");
        response.Headers.WwwAuthenticate.Should().BeEmpty();
        response.Content.Headers.Allow.Should().BeEmpty();
        body.Should().NotBeNullOrWhiteSpace("Step 16 freezes a correlated 404 Problem Details body");
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().Should().Be("resource_not_found");
        body.Should().NotContainAny("controller", "handler", "repository", "table", "CompanyPolicy");
    }

    private static IEnumerable<Operation> Operations(JsonElement root)
    {
        foreach (var path in root.GetProperty("paths").EnumerateObject())
        foreach (var method in path.Value.EnumerateObject())
        {
            if (method.Name is "get" or "post" or "put" or "delete" or "patch")
                yield return new(path.Name, method.Name.ToUpperInvariant(), method.Value);
        }
    }

    private static string[] SecurityNames(JsonElement operation)
    {
        if (!operation.TryGetProperty("security", out var security) ||
            security.GetArrayLength() == 0)
            return [];
        return security[0].EnumerateObject().Select(x => x.Name).ToArray();
    }

    private static string[] ExpectedCustomerSecurity(string method, string path)
    {
        if (path == "/api/lahza/webhook") return ["LahzaSignature"];
        if (path == "/api/v1/devices/register") return [];
        if (path.StartsWith("/api/v1/internal/bookings", StringComparison.Ordinal))
            return ["HmacServiceId", "HmacTimestamp", "HmacNonce", "HmacSignature"];
        if (path is "/api/v1/bookings/from-draft" or "/api/v1/payments/intents" or
            "/api/v1/payments/{id}" or "/api/v1/payments/{id}/verify")
            return ["CustomerBearer", "DeviceToken"];
        if (path.StartsWith("/api/v1/", StringComparison.Ordinal))
            return ["DeviceToken"];
        if (path is "/api/Health" or "/api/Health/db" or "/api/Auth/register" or
            "/api/Auth/login" or "/api/Auth/validate" or "/api/Auth/external-login" or
            "/api/Auth/external-login-callback" ||
            (path == "/api/Users" && method == "POST"))
            return [];
        return ["CustomerBearer"];
    }

    private static TheoryData<string, string> ToTheoryData(IEnumerable<string> operations)
    {
        var data = new TheoryData<string, string>();
        foreach (var operation in operations)
        {
            var split = operation.Split(' ', 2);
            data.Add(split[0], Expand(split[1]));
        }
        return data;
    }

    private readonly record struct Operation(string Path, string Method, JsonElement Value);

    private sealed class CountingBusinessApiClient : IBusinessApiClient
    {
        public int TotalCalls;
        public Task<CatalogSnapshotResponse> GetCatalogSnapshotAsync(
            Guid companyId, CancellationToken cancellationToken = default) =>
            Throw<CatalogSnapshotResponse>();
        public Task<ValidateAppointmentResponse> ValidateAppointmentAsync(
            ValidateAppointmentRequest request, string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Throw<ValidateAppointmentResponse>();
        public Task<AvailableSlotsResponse> GetAvailableSlotsAsync(
            AvailableSlotsRequest request,
            CancellationToken cancellationToken = default) =>
            Throw<AvailableSlotsResponse>();
        public Task<CreateReservationResponse> CreateReservationAsync(
            CreateReservationRequest request, string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Throw<CreateReservationResponse>();
        public Task<AuthoritativeBookingStatusResponse?> GetReservationStatusAsync(
            Guid bookingReference, CancellationToken cancellationToken = default) =>
            Throw<AuthoritativeBookingStatusResponse?>();
        private Task<T> Throw<T>()
        {
            Interlocked.Increment(ref TotalCalls);
            throw new InvalidOperationException("A removed or wrong-host route invoked outbound work.");
        }
    }
}
