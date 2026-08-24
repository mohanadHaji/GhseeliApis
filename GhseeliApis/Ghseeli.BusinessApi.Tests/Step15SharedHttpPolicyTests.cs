using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests;

/// <summary>
/// Defines the host-neutral Step 15 response-policy and safe status contract.
/// </summary>
public class Step15SharedHttpPolicyTests
{
    [Fact]
    public async Task ApiResponse_CarriesCorrelationAndRestrictiveSecurityHeaders()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("X-Correlation-Id", "step15-shared-valid");

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Header(response, "X-Correlation-Id").Should().Be("step15-shared-valid");
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
        Header(response, "Content-Security-Policy").Should().NotBeNullOrWhiteSpace();
        Header(response, "Permissions-Policy").Should().NotBeNullOrWhiteSpace();
        response.Headers.TryGetValues("Strict-Transport-Security", out _).Should()
            .BeFalse("development HTTP must not emit HSTS");
    }

    [Fact]
    public async Task ProductionHttpsResponse_CarriesHsts()
    {
        using var factory = CreateFactory("Production");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/health");

        response.EnsureSuccessStatusCode();
        Header(response, "Strict-Transport-Security").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ProductionHttpsProblem_CarriesHstsAndFullApiPermissionsPolicy()
    {
        using var factory = CreateFactory("Production");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/v1/step15-not-found");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Header(response, "Strict-Transport-Security").Should().NotBeNullOrWhiteSpace();
        Header(response, "Permissions-Policy").Should()
            .Be("camera=(), microphone=(), geolocation=(), payment=()");
    }

    [Fact]
    public async Task UnknownRoute_ReturnsSafeCorrelatedNoStoreProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/step15-shared/unknown");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should()
            .Be("application/problem+json");
        Header(response, "Cache-Control").Should().Be("no-store");
        response.Headers.Contains("ETag").Should().BeFalse();
        response.Content.Headers.Contains("Last-Modified").Should().BeFalse();
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("type").GetString().Should()
            .Be("https://api.ghseeli.example/errors/resource_not_found");
        root.GetProperty("title").GetString().Should().Be("تعذر إكمال الطلب.");
        root.GetProperty("status").GetInt32().Should().Be(404);
        root.GetProperty("detail").GetString().Should().Be("المورد المطلوب غير موجود.");
        root.GetProperty("code").GetString().Should().Be("resource_not_found");
        root.GetProperty("correlationId").GetString().Should()
            .Be(Header(response, "X-Correlation-Id"));
        root.GetProperty("language").GetString().Should().Be("ar");
        Header(response, "Content-Language").Should().Be("ar");
        Header(response, "Vary").Should().Contain("Accept-Language");
    }

    [Fact]
    public async Task HealthWrongMethod_RemainsLanguageNeutralAndKeepsAllowHeader()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/health", new { });

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        Header(response, "Allow").Should().Contain(HttpMethod.Get.Method);
        response.Content.Headers.Contains("Content-Language").Should().BeFalse();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().BeEmpty();
        body.Should().NotContain("EndpointMiddleware");
    }

    [Fact]
    public async Task UnhandledException_ReturnsSafeLocalized500WithoutSensitiveDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/step15-shared/throw");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should()
            .Be("application/problem+json");
        body.Should().Contain("\"code\":\"unexpected_error\"");
        body.Should().Contain("\"detail\":\"حدث خطأ غير متوقع.\"");
        body.Should().NotContain("Server=");
        body.Should().NotContain("eyJ");
        body.Should().NotContain("X-Ghseeli-Signature");
        body.Should().NotContain("Stripe-Signature");
        body.Should().NotContain("person@example.com");
        body.Should().NotContain("InvalidOperationException");
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment = "Development") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting(
                "ConnectionStrings:BusinessConnection",
                "Server=(localdb)\\MSSQLLocalDB;Database=GhseeliBusiness_Step15Shared;Trusted_Connection=True;TrustServerCertificate=True");
            builder.UseSetting(
                "BusinessJwtSettings:SecretKey",
                "Step15SharedBusinessJwtSecret_Minimum32Characters");
            builder.UseSetting("BusinessJwtSettings:Issuer", "Step15.Shared.Tests");
            builder.UseSetting("BusinessJwtSettings:Audience", "Step15.Shared.Clients");
            builder.UseSetting(
                "CustomerBookingStatusClient:BaseUrl",
                "https://customer.example");
            builder.UseSetting(
                "CustomerBookingStatusClient:ServiceId",
                "step15-shared-business");
            builder.UseSetting(
                "CustomerBookingStatusClient:ActiveSecret",
                "Step15SharedCallbackSecret_Minimum32Characters");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:BusinessConnection"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=GhseeliBusiness_Step15Shared;Trusted_Connection=True;TrustServerCertificate=True",
                    ["BusinessJwtSettings:SecretKey"] =
                        "Step15SharedBusinessJwtSecret_Minimum32Characters",
                    ["BusinessJwtSettings:Issuer"] = "Step15.Shared.Tests",
                    ["BusinessJwtSettings:Audience"] = "Step15.Shared.Clients",
                    ["CustomerBookingStatusClient:BaseUrl"] = "https://customer.example",
                    ["CustomerBookingStatusClient:ServiceId"] = "step15-shared-business",
                    ["CustomerBookingStatusClient:ActiveSecret"] =
                        "Step15SharedCallbackSecret_Minimum32Characters"
                });
            });
            builder.ConfigureServices(services =>
                services.AddControllers()
                    .AddApplicationPart(typeof(Step15SharedFailureController).Assembly));
        });

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? string.Join(", ", values)
            : response.Content.Headers.TryGetValues(name, out values)
                ? string.Join(", ", values)
                : null;
}

[ApiController]
[Route("step15-shared")]
public sealed class Step15SharedFailureController : ControllerBase
{
    [HttpGet("throw")]
    public IActionResult Throw() =>
        throw new InvalidOperationException(
            "Server=secret; eyJ.jwt X-Ghseeli-Signature Stripe-Signature person@example.com");
}
