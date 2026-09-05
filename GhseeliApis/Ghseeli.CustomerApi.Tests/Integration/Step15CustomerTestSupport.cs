using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Devices;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class Step15CustomerCollection : ICollectionFixture<Step15CustomerFixture>
{
    public const string Name = "Step15 Customer HTTP";
}

public sealed class Step15CustomerFixture : IAsyncLifetime
{
    public const string DeviceToken =
        "eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHg";
    public const string ExpiredDeviceToken =
        "ZXh4ZXh4ZXh4ZXh4ZXh4ZXh4ZXh4ZXh4ZXh4ZXh4ZXg";
    public const string RotatedDeviceToken =
        "b2xkb2xkb2xkb2xkb2xkb2xkb2xkb2xkb2xkb2xkb2w";
    private static readonly Guid CompanyId =
        Guid.Parse("15151515-1515-1515-1515-151515151515");

    public Step15CustomerFixture()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(
            CompanyId,
            version: 15,
            companyNameAr: "شركة الاختبار",
            companyNameHe: null);
        Factory = new Step15CustomerApiFactory(
            snapshot,
            [
                CatalogTestSupport.CreateDevice(DeviceToken, DateTimeOffset.UtcNow.AddDays(30)),
                CatalogTestSupport.CreateDevice(ExpiredDeviceToken, DateTimeOffset.UtcNow.AddDays(-1))
            ]);
    }

    public Step15CustomerApiFactory Factory { get; }

    public HttpClient CreateClient() => Factory.CreateApiClient();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await Factory.DisposeAsync();
}

public sealed class Step15CustomerApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"Step15Customer-{Guid.NewGuid():N}";
    private readonly CatalogSnapshotResponse _snapshot;
    private readonly IReadOnlyCollection<GhseeliApis.Models.CustomerDevice> _devices;
    private readonly ScriptedBusinessApiClient _businessApiClient = new();
    private readonly Action<IServiceCollection>? _configureServices;

    public Step15CustomerApiFactory(
        CatalogSnapshotResponse snapshot,
        IReadOnlyCollection<GhseeliApis.Models.CustomerDevice> devices,
        Action<IServiceCollection>? configureServices = null)
    {
        _snapshot = snapshot;
        _devices = devices;
        _configureServices = configureServices;
        _businessApiClient.GetCatalogSnapshotHandler = (_, _) => Task.FromResult(snapshot);
        _businessApiClient.ValidateAppointmentHandler = (request, _, _) =>
            Task.FromResult(CatalogTestSupport.CreateValidationResponse(snapshot, request));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:CustomerConnection", "unused-by-step15-in-memory");
        builder.UseSetting("JwtSettings:SecretKey", "CheckoutDraftApiTestsSecret_Minimum32Chars");
        builder.UseSetting("JwtSettings:Issuer", "GhseeliApis.CheckoutDraftTests");
        builder.UseSetting("JwtSettings:Audience", "GhseeliApis.CheckoutDraftClients");
        builder.UseSetting("Swagger:Enabled", "true");
        builder.UseSetting("CatalogReadModel:Providers:0:SourceCompanyId",
            _snapshot.Company.Id.ToString());
        builder.UseSetting("CatalogReadModel:Providers:0:Enabled", "true");
        builder.UseSetting("CatalogReadModel:Providers:0:Order", "0");
        builder.UseSetting("CheckoutDrafts:LifetimeMinutes", "30");
        builder.UseSetting("CatalogReadModel:FreshWindowSeconds", "300");
        builder.UseSetting("CatalogReadModel:MaxStaleWindowSeconds", "3600");
        builder.UseSetting("CatalogReadModel:LeaseDurationSeconds", "30");
        builder.UseSetting("CheckoutPricing:Currency", "ILS");
        builder.UseSetting("CheckoutPricing:TaxRatePercent", "0");
        builder.UseSetting("CheckoutPricing:TaxAppliesToServiceFee", "false");
        builder.UseSetting("CheckoutPricing:ServiceFee:Mode", "None");
        builder.UseSetting("Lahza:SecretKey", "");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<IBusinessApiClient>();
            services.AddSingleton<IBusinessApiClient>(_ => _businessApiClient);
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
            _configureServices?.Invoke(services);

            using var scope = services.BuildServiceProvider().CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
            context.CustomerDevices.AddRange(_devices);
            CheckoutDraftTestSupport.SeedSnapshotAsync(context, _snapshot).GetAwaiter().GetResult();
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    internal ScriptedBusinessApiClient BusinessApiClient => _businessApiClient;
}

internal static class Step15CustomerAssertions
{
    private const string JwtSecret = "CheckoutDraftApiTestsSecret_Minimum32Chars";

    internal static HttpRequestMessage Request(
        HttpMethod method,
        string path,
        bool device = false,
        string? acceptLanguage = null,
        string? bearer = null,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        if (device)
        {
            request.Headers.TryAddWithoutValidation(
                DeviceTokenDefaults.HeaderName,
                Step15CustomerFixture.DeviceToken);
        }

        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return request;
    }

    internal static string Jwt(string role = "User", DateTime? expires = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
            SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "GhseeliApis.CheckoutDraftTests",
            audience: "GhseeliApis.CheckoutDraftClients",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role)
            ],
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials));
    }

    internal static async Task<JsonDocument> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotBeNullOrWhiteSpace();
        return JsonDocument.Parse(text);
    }

    internal static void ExactProblem(
        HttpResponseMessage response,
        JsonElement problem,
        int status,
        string code,
        string title,
        string detail,
        string? language)
    {
        response.StatusCode.Should().Be((System.Net.HttpStatusCode)status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        problem.GetProperty("type").GetString()
            .Should().Be($"https://api.ghseeli.example/errors/{code}");
        problem.GetProperty("title").GetString().Should().Be(title);
        problem.GetProperty("status").GetInt32().Should().Be(status);
        problem.GetProperty("detail").GetString().Should().Be(detail);
        problem.GetProperty("code").GetString().Should().Be(code);
        var correlation = problem.GetProperty("correlationId").GetString();
        correlation.Should().NotBeNullOrWhiteSpace();
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle(correlation!);

        if (language is null)
        {
            problem.TryGetProperty("language", out _).Should().BeFalse();
            response.Content.Headers.ContentLanguage.Should().BeEmpty();
        }
        else
        {
            problem.GetProperty("language").GetString().Should().Be(language);
            response.Content.Headers.ContentLanguage.Should().ContainSingle(language);
        }

        AssertNoStoreAndNoLeakage(response, problem.GetRawText());
    }

    internal static void GenericProblem(
        HttpResponseMessage response,
        JsonElement problem,
        int status,
        string code,
        string language = "ar")
    {
        var hebrew = language == "he";
        var detail = (code, hebrew) switch
        {
            ("language_invalid", false) => "اللغة المطلوبة غير مدعومة.",
            ("language_invalid", true) => "השפה המבוקשת אינה נתמכת.",
            ("request_invalid", false) => "الطلب غير صالح.",
            ("request_invalid", true) => "הבקשה אינה חוקית.",
            ("customer_authentication_required", false) => "مطلوب تسجيل دخول العميل.",
            ("customer_authentication_required", true) => "נדרש אימות לקוח.",
            ("customer_authorization_forbidden", false) => "لا يملك العميل صلاحية تنفيذ هذا الطلب.",
            ("customer_authorization_forbidden", true) => "ללקוח אין הרשאה לבצע בקשה זו.",
            ("resource_not_found", false) => "المورد المطلوب غير موجود.",
            ("resource_not_found", true) => "המשאב המבוקש לא נמצא.",
            ("method_not_allowed", false) => "طريقة HTTP غير مسموحة لهذا المسار.",
            ("method_not_allowed", true) => "שיטת HTTP אינה מותרת עבור נתיב זה.",
            ("request_body_too_large", false) => "حجم نص الطلب يتجاوز الحد المسموح.",
            ("request_body_too_large", true) => "גוף הבקשה חורג מהמגבלה המותרת.",
            ("unsupported_media_type", false) => "نوع محتوى الطلب غير مدعوم.",
            ("unsupported_media_type", true) => "סוג התוכן של הבקשה אינו נתמך.",
            ("unexpected_error", false) => "حدث خطأ غير متوقع.",
            ("unexpected_error", true) => "אירעה שגיאה בלתי צפויה.",
            ("service_unavailable", false) => "الخدمة غير متاحة مؤقتًا.",
            ("service_unavailable", true) => "השירות אינו זמין זמנית.",
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
        };
        ExactProblem(
            response,
            problem,
            status,
            code,
            hebrew ? "לא ניתן להשלים את הבקשה." : "تعذر إكمال الطلب.",
            detail,
            language);
    }

    internal static void AssertNoStoreAndNoLeakage(HttpResponseMessage response, string body)
    {
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.ETag.Should().BeNull();
        response.Content.Headers.LastModified.Should().BeNull();
        response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse();
        body.Should().NotContainAny(
            "System.",
            "Exception",
            "StackTrace",
            "Server=",
            JwtSecret,
            Step15CustomerFixture.DeviceToken,
            "secret@example.com",
            "super-secret",
            "sk_test_");
    }

    internal static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle("no-referrer");
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle();
        response.Headers.GetValues("Permissions-Policy").Should().ContainSingle();
    }
}
