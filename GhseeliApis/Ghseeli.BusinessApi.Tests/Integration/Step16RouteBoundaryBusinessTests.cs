using System.Collections;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ghseeli.BusinessApi.Tests.Integration;

/// <summary>
/// Step 16 red contracts for the Business host's exact route responsibility boundary.
/// </summary>
public sealed class Step16RouteBoundaryBusinessTests : IClassFixture<CatalogApiFactory>
{
    private static readonly Guid Id = Guid.Parse("16161616-1616-1616-1616-161616161616");
    private readonly CatalogApiFactory _factory;

    public Step16RouteBoundaryBusinessTests(CatalogApiFactory factory) => _factory = factory;

    [Fact(DisplayName = "STEP16-ROUTES-BUSINESS-EXACT-066 STEP16-BUSINESS-REGRESSION-112")]
    public void ControllerActionDiscovery_ContainsEveryAndOnlyRetainedBusinessOperation()
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

        actual.Should().Equal(BusinessOperations
            .Where(operation => operation != "GET /api/health")
            .Order(StringComparer.Ordinal),
            "Business health is a retained minimal endpoint rather than a controller action");
    }

    [Fact(DisplayName = "STEP16-SWAGGER-BUSINESS-EXACT-068 STEP16-BUSINESS-REGRESSION-112")]
    public async Task OpenApi_ContainsEveryRetainedBusinessOperation_WithExactSecurity()
    {
        using var client = _factory.CreateSecureClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operations = Operations(document.RootElement).ToArray();

        operations.Select(x => $"{x.Method} {x.Path}").Order(StringComparer.Ordinal)
            .Should().Equal(BusinessOperations.Order(StringComparer.Ordinal));
        foreach (var operation in operations)
        {
            SecurityNames(operation.Value).Should().Equal(
                ExpectedSecurity(operation.Path),
                $"STEP16-SWAGGER-BUSINESS-EXACT-068 security for {operation.Method} {operation.Path}");
        }
    }

    [Theory]
    [MemberData(nameof(CustomerWrongHostOperations))]
    public async Task EveryCustomerOperation_OnBusinessHost_IsPreAuth404WithNoSideEffects(
        string scenarioId,
        string method,
        string path)
    {
        var before = StateSnapshot();
        using var client = _factory.CreateAuthenticatedClient(_factory.AdminUserId, BusinessRoles.Admin);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Device-Token", new string('x', 43));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Stripe-Signature", "not-consumed");
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"{scenarioId} requires {method} {path} to miss routing before every auth mechanism");
        response.Headers.WwwAuthenticate.Should().BeEmpty();
        response.Content.Headers.Allow.Should().BeEmpty();
        body.Should().NotBeNullOrWhiteSpace("Step 16 freezes a correlated 404 Problem Details body");
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().Should().Be("resource_not_found");
        body.Should().NotContainAny("controller", "handler", "repository", "table");
        StateSnapshot().Should().Equal(before,
            $"{scenarioId} forbids Identity, nonce, idempotency, outbox, version, and domain effects");
    }

    [Fact(DisplayName = "STEP16-HEALTH-BUSINESS-061 STEP16-BUSINESS-REGRESSION-112")]
    public async Task EveryBusinessManagementAndInternalFamily_IsPositivelyInventoried_AndHealthHeadWorks()
    {
        BusinessOperations.Should().Contain(operation =>
            operation.StartsWith("GET /api/v1/business/company", StringComparison.Ordinal));
        BusinessOperations.Should().Contain(operation =>
            operation.Contains("/catalog/categories", StringComparison.Ordinal));
        BusinessOperations.Should().Contain(operation =>
            operation.Contains("/catalog/offerings", StringComparison.Ordinal));
        BusinessOperations.Should().Contain(operation =>
            operation.Contains("/addon-groups", StringComparison.Ordinal));
        BusinessOperations.Should().Contain(operation =>
            operation.Contains("/addon-choices", StringComparison.Ordinal));
        BusinessOperations.Should().Contain(operation =>
            operation.Contains("/availability/", StringComparison.Ordinal));
        BusinessOperations.Should().Contain(operation =>
            operation.Contains("/work-orders/", StringComparison.Ordinal));
        BusinessOperations.Should().Contain(operation =>
            operation.Contains("/booking-status-outbox/", StringComparison.Ordinal));
        BusinessOperations.Count(operation =>
            operation.Contains("/api/v1/internal/", StringComparison.Ordinal)).Should().Be(4);

        using var client = _factory.CreateSecureClient();
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            using var response = await client.SendAsync(new HttpRequestMessage(method, "/api/health"));
            response.StatusCode.Should().Be(
                HttpStatusCode.OK,
                "health response was {0}",
                await response.Content.ReadAsStringAsync());
        }
    }

    public static TheoryData<string, string, string> CustomerWrongHostOperations
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var operation in CustomerOperations)
            {
                var split = operation.Split(' ', 2);
                data.Add(ScenarioFor(split[1]), split[0], Expand(split[1]));
            }
            return data;
        }
    }

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

    private static readonly string[] CustomerOperations =
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
        "GET /api/v1/catalog/offerings/{id}", "POST /api/v1/checkout/drafts",
        "GET /api/v1/checkout/drafts/{orderGuid}",
        "PUT /api/v1/checkout/drafts/{orderGuid}", "POST /api/v1/pricing/reprice",
        "POST /api/v1/checkout/reprice", "POST /api/v1/bookings/from-draft",
        "POST /api/v1/payments/intents", "GET /api/v1/payments/{id}",
        "POST /api/v1/internal/bookings/status",
        "POST /api/v1/internal/bookings/{reference}/reconcile",
        "GET /api/v1/internal/bookings/{reference}", "POST /api/stripe/webhook",
        "GET /api/Health", "GET /api/HEALTH", "GET /API/HEALTH",
        "GET /Api/health", "GET /api/HeAlTh/", "GET /api/Health/db",
        "GET /api/Bookings/my-bookings", "GET /api/Bookings/my-bookings/upcoming",
        "GET /api/Bookings/my-bookings/history", "GET /api/Bookings/company/{id}",
        "GET /api/Bookings/{id}", "POST /api/Bookings", "PUT /api/Bookings/{id}",
        "PUT /api/Bookings/{id}/cancel", "PUT /api/Bookings/{id}/confirm",
        "PUT /api/Bookings/{id}/start", "PUT /api/Bookings/{id}/complete",
        "GET /api/Bookings/check-availability",
        "GET /api/Payments", "GET /api/Payments/{id}", "GET /api/Payments/my-payments",
        "GET /api/Payments/booking/{id}", "POST /api/Payments",
        "PUT /api/Payments/{id}/status", "POST /api/Payments/{id}/refund"
    ];

    private string[] StateSnapshot()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
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

    private static string ScenarioFor(string path) =>
        path.StartsWith("/api/Auth", StringComparison.Ordinal) ||
        path.StartsWith("/api/Users", StringComparison.Ordinal)
            ? "STEP16-FOREIGN-CUSTOMER-AUTH-USERS-127"
            : path.StartsWith("/api/Addresses", StringComparison.Ordinal) ||
              path.StartsWith("/api/Vehicles", StringComparison.Ordinal)
                ? "STEP16-FOREIGN-ADDRESS-VEHICLE-128"
                : path.StartsWith("/api/v1/devices", StringComparison.Ordinal) ||
                  path.StartsWith("/api/v1/configuration", StringComparison.Ordinal)
                    ? "STEP16-FOREIGN-DEVICE-CONFIG-129"
                    : path.StartsWith("/api/v1/catalog", StringComparison.Ordinal)
                        ? "STEP16-FOREIGN-CUSTOMER-CATALOG-130"
                        : path.StartsWith("/api/v1/checkout", StringComparison.Ordinal) ||
                          path.StartsWith("/api/v1/pricing", StringComparison.Ordinal)
                            ? "STEP16-FOREIGN-DRAFT-PRICING-131"
                            : path.Contains("booking", StringComparison.OrdinalIgnoreCase)
                                ? "STEP16-FOREIGN-CUSTOMER-BOOKING-132"
                                : path.Contains("payment", StringComparison.OrdinalIgnoreCase) ||
                                  path.StartsWith("/api/stripe", StringComparison.Ordinal)
                                    ? "STEP16-FOREIGN-PAYMENTS-STRIPE-133"
                                    : path.StartsWith("/api/v1/internal", StringComparison.Ordinal)
                                        ? "STEP16-FOREIGN-CUSTOMER-INTERNAL-134"
                                        : "STEP16-FOREIGN-HEALTH-NAMES-135";

    private static string Expand(string path) => path
        .Replace("{id}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{orderGuid}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{reference}", Id.ToString("D"), StringComparison.Ordinal)
        .Replace("{provider}", "google", StringComparison.Ordinal);

    private static string NormalizeTemplate(string template) =>
        template.Replace(":guid}", "}", StringComparison.Ordinal);

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

    private static string[] ExpectedSecurity(string path)
    {
        if (path is "/api/v1/business/auth/register-owner" or
            "/api/v1/business/auth/login" or "/api/health")
            return [];
        if (path.StartsWith("/api/v1/internal/", StringComparison.Ordinal))
            return ["HmacServiceId", "HmacTimestamp", "HmacNonce", "HmacSignature"];
        return ["BusinessBearer"];
    }

    private readonly record struct Operation(string Path, string Method, JsonElement Value);
}
