using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Devices;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.DataPartitioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Exercises the real customer HTTP pipeline for device-protected configuration retrieval.
/// </summary>
public class ConfigurationApiIntegrationTests
{
    private const string ArabicDisplayName = "غسيلي";
    private const string ArabicLegalNotice =
        "باستخدام التطبيق فإنك توافق على الشروط وسياسة الخصوصية الحالية.";
    private const string HebrewLegalNotice =
        "השימוש באפליקציה כפוף לתנאים ולמדיניות הפרטיות הנוכחיים.";
    private const string InvalidCorrelationId =
        "corr-step8-overlong-correlation-id-that-must-be-replaced-before-roundtrip-20260821190846";
    internal const string JwtSecret = "CustomerConfigurationTestsSecret_Minimum32Chars";
    internal const string JwtIssuer = "GhseeliApis.ConfigurationTests";
    internal const string JwtAudience = "GhseeliApis.ConfigurationClients";

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-CONFIG-001")]
    public async Task GetConfiguration_WithoutDeviceToken_ReturnsAnonymousProductionConfiguration()
    {
        using var factory = CreateFactory(seedActiveConfiguration: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration?language=he",
            acceptLanguage: "ar");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        document.RootElement.GetProperty("legal").GetProperty("notice").GetString()
            .Should().Be(HebrewLegalNotice);
    }

    [Fact]
    public async Task GetConfiguration_WithProductionJwtOnly_ReturnsProductionConfiguration()
    {
        using var factory = CreateFactory(seedActiveConfiguration: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(HttpMethod.Get, "/api/v1/configuration");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(DataPartitionNames.Production));

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("legal").GetProperty("notice").GetString()
            .Should().Be(ArabicLegalNotice);
    }

    [Fact]
    public async Task GetConfiguration_WithMatchingProductionJwtAndDevice_ReturnsProduction()
    {
        var token = Token(30);
        using var factory = CreateFactory(
            seedActiveConfiguration: true,
            devices: [CreateDevice(token, DateTimeOffset.UtcNow.AddDays(30))]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            token);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(DataPartitionNames.Production));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetConfiguration_WithValidDevice_UpdatesLastSeenForTrustedPartition(
        bool isDemo)
    {
        var token = Token(isDemo ? (byte)32 : (byte)31);
        var previousLastSeen = DateTimeOffset.UtcNow.AddDays(-3);
        var device = CreateDevice(token, DateTimeOffset.UtcNow.AddDays(30));
        device.IsDemo = isDemo;
        device.LastSeenAt = previousLastSeen;
        using var factory = CreateFactory(
            seedActiveConfiguration: true,
            devices: [device],
            useDemoData: isDemo);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            token);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(isDemo
                ? DataPartitionNames.Demo
                : DataPartitionNames.Production));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICustomerDataPartitionContext>()
            .SetTrustedPartition(isDemo
                ? DataPartitionNames.Demo
                : DataPartitionNames.Production);
        var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .CustomerDevices.AsNoTracking().SingleAsync();
        stored.LastSeenAt.Should().NotBeNull();
        stored.LastSeenAt.Should().BeAfter(previousLastSeen);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-006")]
    public async Task GetConfiguration_WithMalformedDeviceToken_AndMalformedLanguageHeader_FallsBackToArabicProblem()
    {
        using var factory = CreateFactory(seedActiveConfiguration: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            deviceToken: "short-token",
            acceptLanguage: "-, ;q=1");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertProblem(
            document.RootElement,
            HttpStatusCode.Unauthorized,
            DeviceProblemCodes.TokenInvalid,
            ConfigurationLanguageResolver.Arabic);
        document.RootElement.GetProperty("title").GetString().Should().Be("فشل التحقق من الجهاز.");
        document.RootElement.GetProperty("detail").GetString().Should().Be("رمز الجهاز غير صالح.");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-DEV-DEMO-CONFIG-001")]
    public async Task GetConfiguration_DemoOnlyEnvironment_IgnoresCredentials()
    {
        using var factory = CreateFactory(
            seedActiveConfiguration: true,
            useDemoData: true,
            demoPublicApisOnly: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            deviceToken: "malformed");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(DataPartitionNames.Production));

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("display").GetProperty("name")
            .GetString().Should().Be(ArabicDisplayName);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-024")]
    public async Task GetConfiguration_WithUnknownDeviceToken_ReturnsLocalizedHebrewProblem()
    {
        using var factory = CreateFactory(seedActiveConfiguration: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            deviceToken: Token(1),
            acceptLanguage: "he-IL,ar;q=0.8");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertProblem(
            document.RootElement,
            HttpStatusCode.Unauthorized,
            DeviceProblemCodes.TokenInvalid,
            ConfigurationLanguageResolver.Hebrew);
        document.RootElement.GetProperty("detail").GetString().Should().Be("אסימון המכשיר אינו תקין.");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-025")]
    public async Task GetConfiguration_WithExpiredDeviceToken_ReturnsLocalizedExpiredProblem()
    {
        var expiredToken = Token(2);
        using var factory = CreateFactory(
            seedActiveConfiguration: true,
            devices: [CreateDevice(expiredToken, DateTimeOffset.UtcNow.AddMinutes(-1))]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            deviceToken: expiredToken);

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertProblem(
            document.RootElement,
            HttpStatusCode.Unauthorized,
            DeviceProblemCodes.TokenExpired,
            ConfigurationLanguageResolver.Arabic);
        document.RootElement.GetProperty("detail").GetString().Should().Be("انتهت صلاحية رمز الجهاز.");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-DEMO-005")]
    public async Task GetConfiguration_WithDemoDevice_ReturnsDemoConfiguration()
    {
        const string demoDisplayName = "DEMO-CONFIGURATION-NAME";
        var token = Token(24);
        var device = CreateDevice(token, DateTimeOffset.UtcNow.AddDays(30));
        device.IsDemo = true;
        using var factory = new CustomerConfigurationApiFactory(context =>
        {
            var demoConfiguration = CreateConfiguration();
            demoConfiguration.DisplayNameAr = demoDisplayName;
            demoConfiguration.SupportEmail = "demo-config@example.test";
            context.CustomerConfigurations.Add(demoConfiguration);
            context.CustomerDevices.Add(device);
        }, useDemoData: true);
        using var client = factory.CreateApiClient();

        using var response = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            token));
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("display").GetProperty("name").GetString()
            .Should().Be(demoDisplayName);
        document.RootElement.GetProperty("support").GetProperty("email").GetString()
            .Should().Be("demo-config@example.test");
        document.RootElement.GetRawText().Should().NotContain(ArabicDisplayName);
        document.RootElement.GetRawText().Should().NotContain("step8-config@example.test");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-026")]
    public Task GetConfiguration_WithInactiveDeviceToken_ReturnsExactInactiveCode() =>
        AssertInactiveOrRotatedDeviceRejectedAsync("inactive", DeviceProblemCodes.TokenInactive);

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-027")]
    public Task GetConfiguration_WithRotatedDeviceToken_ReturnsExactInvalidCode() =>
        AssertInactiveOrRotatedDeviceRejectedAsync("rotated", DeviceProblemCodes.TokenInvalid);

    private static async Task AssertInactiveOrRotatedDeviceRejectedAsync(
        string state,
        string expectedCode)
    {
        var suppliedToken = Token(20);
        var device = CreateDevice(
            state == "rotated" ? Token(21) : suppliedToken,
            DateTimeOffset.UtcNow.AddDays(30));
        device.IsActive = state != "inactive";
        using var factory = CreateFactory(
            seedActiveConfiguration: true,
            devices: [device]);
        using var client = factory.CreateApiClient();

        using var response = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            suppliedToken));
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString().Should().Be(expectedCode);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-DUPLICATE-007")]
    public async Task GetConfiguration_WithDuplicateDeviceHeaders_RejectsWithoutFallback()
    {
        using var factory = CreateFactory(seedActiveConfiguration: true);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/configuration");
        request.Headers.TryAddWithoutValidation(
            DeviceTokenDefaults.HeaderName,
            new[] { Token(22), Token(23) });

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(DeviceProblemCodes.TokenInvalid);
    }

    [Fact]
    public async Task GetConfiguration_DefaultsToArabicWhenNoLanguageSelectorsAreSupplied()
    {
        var validToken = Token(3);
        using var factory = CreateFactory(
            seedActiveConfiguration: true,
            devices: [CreateDevice(validToken, DateTimeOffset.UtcNow.AddDays(30))]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            deviceToken: validToken);

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
        document.RootElement.GetProperty("display").GetProperty("name").GetString().Should().Be(ArabicDisplayName);
        document.RootElement.GetProperty("legal").GetProperty("notice").GetString().Should().Be(ArabicLegalNotice);
    }

    [Fact]
    public async Task GetConfiguration_UnsupportedAcceptLanguage_FallsBackToArabic()
    {
        var validToken = Token(4);
        using var factory = CreateFactory(
            seedActiveConfiguration: true,
            devices: [CreateDevice(validToken, DateTimeOffset.UtcNow.AddDays(30))]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            deviceToken: validToken,
            acceptLanguage: "en-US,en;q=0.9");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
        document.RootElement.GetProperty("legal").GetProperty("notice").GetString().Should().Be(ArabicLegalNotice);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CONFIGURATION-059")]
    public async Task GetConfiguration_WithValidDeviceToken_ReturnsLocalizedConfiguration()
    {
        var validToken = Token(5);
        using var factory = CreateFactory(
            seedActiveConfiguration: true,
            devices: [CreateDevice(validToken, DateTimeOffset.UtcNow.AddDays(30))]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration?language=he",
            deviceToken: validToken,
            acceptLanguage: "ar");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        document.RootElement.GetProperty("display").GetProperty("name").GetString().Should().Be(ArabicDisplayName);
        document.RootElement.GetProperty("legal").GetProperty("notice").GetString().Should().Be(HebrewLegalNotice);
        response.Headers.TryGetValues("X-Correlation-Id", out var correlationValues).Should().BeTrue();
        correlationValues.Should().ContainSingle();
    }

    [Fact]
    public async Task GetConfiguration_InvalidLanguageOverride_WithMalformedHeaderAndSupportedWeightedLanguage_ReturnsLocalizedProblem()
    {
        var validToken = Token(6);
        using var factory = CreateFactory(
            seedActiveConfiguration: true,
            devices: [CreateDevice(validToken, DateTimeOffset.UtcNow.AddDays(30))]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration?language=-",
            deviceToken: validToken,
            acceptLanguage: "-, ;q=1, he-IL;q=0.8");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        AssertProblem(
            document.RootElement,
            HttpStatusCode.BadRequest,
            "language_invalid",
            ConfigurationLanguageResolver.Arabic);
        document.RootElement.GetProperty("detail").GetString()
            .Should()
            .Be("اللغة المطلوبة غير مدعومة.");
    }

    [Fact]
    public async Task GetConfiguration_InvalidCorrelationId_IsSanitizedInHeaderAndBody()
    {
        var validToken = Token(7);
        using var factory = CreateFactory(
            seedActiveConfiguration: true,
            devices: [CreateDevice(validToken, DateTimeOffset.UtcNow.AddDays(30))]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration?language=en",
            deviceToken: validToken,
            acceptLanguage: "ar",
            correlationId: InvalidCorrelationId);

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);
        var correlationId = document.RootElement.GetProperty("correlationId").GetString();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        correlationId.Should().NotBe(InvalidCorrelationId);
        correlationId.Should().MatchRegex("^[0-9a-f]{32}$");
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle(correlationId!);
    }

    [Fact]
    public async Task GetConfiguration_WhenNoActiveConfigurationExists_ReturnsServiceUnavailableProblem()
    {
        var validToken = Token(8);
        using var factory = CreateFactory(
            seedActiveConfiguration: false,
            devices: [CreateDevice(validToken, DateTimeOffset.UtcNow.AddDays(30))]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            deviceToken: validToken,
            acceptLanguage: "he");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        AssertProblem(
            document.RootElement,
            HttpStatusCode.ServiceUnavailable,
            ConfigurationProblemCodes.Unavailable,
            ConfigurationLanguageResolver.Hebrew);
        document.RootElement.GetProperty("detail").GetString()
            .Should().Be("הגדרות היישום טרם הוגדרו.");
    }

    [Fact]
    public async Task GetConfiguration_WithRotatedOldToken_ReturnsInvalidProblem()
    {
        using var factory = CreateFactory(seedActiveConfiguration: true);
        using var client = factory.CreateApiClient();
        var installationId = Guid.NewGuid();

        using var issueResponse = await client.PostAsJsonAsync("/api/v1/devices/register", new
        {
            installationId,
            platform = "iOS",
            appVersion = "1.0.0"
        });
        using var issueDocument = await ReadJsonAsync(issueResponse);
        var issuedToken = issueDocument.RootElement.GetProperty("token").GetString();

        using var rotateRequest = CreateRequest(
            HttpMethod.Post,
            "/api/v1/devices/register",
            deviceToken: issuedToken);
        rotateRequest.Content = JsonContent.Create(new
        {
            installationId,
            platform = "Android",
            appVersion = "1.1.0"
        });

        using var rotateResponse = await client.SendAsync(rotateRequest);
        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var staleRequest = CreateRequest(
            HttpMethod.Get,
            "/api/v1/configuration",
            deviceToken: issuedToken);
        using var staleResponse = await client.SendAsync(staleRequest);
        using var staleDocument = await ReadJsonAsync(staleResponse);

        staleResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertProblem(
            staleDocument.RootElement,
            HttpStatusCode.Unauthorized,
            DeviceProblemCodes.TokenInvalid,
            ConfigurationLanguageResolver.Arabic);
    }

    [Fact]
    public async Task PostConfiguration_ReturnsMethodNotAllowed()
    {
        var validToken = Token(9);
        using var factory = CreateFactory(
            seedActiveConfiguration: true,
            devices: [CreateDevice(validToken, DateTimeOffset.UtcNow.AddDays(30))]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/configuration",
            deviceToken: validToken);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.Should().Contain("GET");
    }

    [Fact]
    public async Task Swagger_ContainsConfigurationEndpointAndLanguageParameters()
    {
        using var factory = CreateFactory(seedActiveConfiguration: false);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var getOperation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/configuration")
            .GetProperty("get");
        var parameters = getOperation.GetProperty("parameters").EnumerateArray().ToArray();
        parameters.Should().Contain(parameter =>
            parameter.GetProperty("name").GetString() == "language" &&
            parameter.GetProperty("in").GetString() == "query");
        parameters.Should().Contain(parameter =>
            parameter.GetProperty("name").GetString() == "Accept-Language" &&
            parameter.GetProperty("in").GetString() == "header");
    }

    private static CustomerConfigurationApiFactory CreateFactory(
        bool seedActiveConfiguration,
        IEnumerable<CustomerDevice>? devices = null,
        bool useDemoData = false,
        bool demoPublicApisOnly = false) =>
        new(context =>
        {
            if (seedActiveConfiguration)
            {
                context.CustomerConfigurations.Add(CreateConfiguration());
            }

            if (devices is not null)
            {
                context.CustomerDevices.AddRange(devices);
            }
        }, useDemoData, demoPublicApisOnly);

    private static CustomerConfiguration CreateConfiguration() =>
        new()
        {
            Id = Guid.NewGuid(),
            IsActive = true,
            SupportEmail = "step8-config@example.test",
            SupportPhone = "+972500000001",
            DisplayNameAr = ArabicDisplayName,
            DisplayNameHe = null,
            LegalNoticeAr = ArabicLegalNotice,
            LegalNoticeHe = HebrewLegalNotice,
            PrivacyPolicyUrl = "https://example.test/privacy",
            TermsOfServiceUrl = "https://example.test/terms",
            IsMaintenanceModeEnabled = false
        };

    private static CustomerDevice CreateDevice(string token, DateTimeOffset expiresAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            InstallationId = Guid.NewGuid(),
            Platform = "iOS",
            AppVersion = "1.0.0",
            TokenHash = DeviceTokenHasher.Hash(token),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresAt = expiresAt
        };

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string? deviceToken = null,
        string? acceptLanguage = null,
        string? correlationId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        if (!string.IsNullOrWhiteSpace(deviceToken))
        {
            request.Headers.TryAddWithoutValidation(DeviceTokenDefaults.HeaderName, deviceToken);
        }

        if (!string.IsNullOrWhiteSpace(acceptLanguage))
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        }

        return request;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload);
    }

    private static void AssertProblem(
        JsonElement root,
        HttpStatusCode expectedStatusCode,
        string expectedCode,
        string expectedLanguage)
    {
        root.GetProperty("status").GetInt32().Should().Be((int)expectedStatusCode);
        root.GetProperty("code").GetString().Should().Be(expectedCode);
        root.GetProperty("language").GetString().Should().Be(expectedLanguage);
        root.GetProperty("correlationId").GetString().Should().MatchRegex("^[0-9a-f]{32}$");
    }

    private static string Token(byte value) =>
        WebEncoders.Base64UrlEncode(Enumerable.Repeat(value, 32).ToArray());

    private static string CreateJwt(string partition)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
            SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            JwtIssuer,
            JwtAudience,
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "User"),
                new Claim(DataPartitionNames.ClaimType, partition)
            ],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials));
    }
}

public sealed class CustomerConfigurationApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"CustomerConfigurationApiTests-{Guid.NewGuid()}";
    private readonly Action<ApplicationDbContext>? _seed;
    private readonly bool _useDemoData;
    private readonly bool _demoPublicApisOnly;

    public CustomerConfigurationApiFactory(
        Action<ApplicationDbContext>? seed = null,
        bool useDemoData = false,
        bool demoPublicApisOnly = false)
    {
        _seed = seed;
        _useDemoData = useDemoData;
        _demoPublicApisOnly = demoPublicApisOnly;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(
            "ConnectionStrings:CustomerConnection",
            "Server=(localdb)\\MSSQLLocalDB;Database=CustomerConfigurationApiTests;Trusted_Connection=True;TrustServerCertificate=True;");
        builder.UseSetting(
            "JwtSettings:SecretKey",
            ConfigurationApiIntegrationTests.JwtSecret);
        builder.UseSetting(
            "JwtSettings:Issuer",
            ConfigurationApiIntegrationTests.JwtIssuer);
        builder.UseSetting(
            "JwtSettings:Audience",
            ConfigurationApiIntegrationTests.JwtAudience);
        builder.UseSetting("Swagger:Enabled", "true");
        builder.UseSetting(
            "DemoData:PublicApisOnly",
            _demoPublicApisOnly.ToString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            using var scope = services.BuildServiceProvider().CreateScope();
            if (_useDemoData)
            {
                scope.ServiceProvider.GetRequiredService<ICustomerDataPartitionContext>()
                    .SetTrustedPartition(DataPartitionNames.Demo);
            }
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
            _seed?.Invoke(context);
            context.SaveChanges();
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
}
