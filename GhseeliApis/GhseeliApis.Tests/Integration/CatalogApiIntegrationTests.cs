using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Devices;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text.Json;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Exercises the real customer HTTP pipeline for catalog browse endpoints.
/// </summary>
public class CatalogApiIntegrationTests
{
    [Fact]
    public async Task GetBusinesses_WithoutDeviceToken_ReturnsLocalizedUnauthorizedProblem()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/api/v1/catalog/businesses?language=he");
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString().Should().Be(DeviceProblemCodes.TokenMissing);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
    }

    [Fact]
    public async Task GetBusinesses_WithMalformedDeviceToken_ReturnsArabicUnauthorizedProblem()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            deviceToken: "short-token",
            acceptLanguage: "-, ;q=1");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString().Should().Be(DeviceProblemCodes.TokenInvalid);
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-BUSINESSES-061")]
    public async Task GetBusinesses_WithValidDeviceToken_ReturnsLocalizedBusinessesAndNoStore()
    {
        var token = CatalogTestSupport.CreateToken(9);
        await using var factory = new CatalogApiFactory(devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(
                companyId == factory.FirstSourceCompanyId
                    ? CatalogTestSupport.CreateSnapshot(
                        companyId,
                        version: 3,
                        companyNameAr: "الأول",
                        companyNameHe: "הראשון")
                    : CatalogTestSupport.CreateSnapshot(
                        companyId,
                        version: 4,
                        companyNameAr: "الثاني",
                        companyNameHe: "השני"));
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            deviceToken: token,
            acceptLanguage: "he");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        var businesses = document.RootElement.GetProperty("businesses").EnumerateArray().ToArray();
        businesses.Should().HaveCount(2);
        businesses[0].GetProperty("sourceId").GetGuid().Should().Be(factory.FirstSourceCompanyId);
        businesses[0].GetProperty("name").GetString().Should().Be("הראשון");
        businesses[0].GetProperty("catalog").GetProperty("version").GetInt64().Should().Be(3);
        businesses[1].GetProperty("catalog").GetProperty("version").GetInt64().Should().Be(4);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-CATEGORIES-060")]
    public async Task GetCategories_WithoutBusinessFilter_ReturnsOwningBusinessContext()
    {
        var token = CatalogTestSupport.CreateToken(10);
        await using var factory = new CatalogApiFactory(devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(
                companyId == factory.FirstSourceCompanyId
                    ? CatalogTestSupport.CreateSnapshot(
                        companyId,
                        version: 1,
                        companyNameAr: "شركة أ")
                    : CatalogTestSupport.CreateSnapshot(
                        companyId,
                        version: 1,
                        companyNameAr: "شركة ب"));
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/categories?language=ar",
            deviceToken: token);

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var categories = document.RootElement.GetProperty("categories").EnumerateArray().ToArray();
        categories.Should().HaveCount(2);
        foreach (var category in categories)
        {
            category.TryGetProperty("business", out var business).Should().BeTrue();
            business.TryGetProperty("id", out _).Should().BeTrue();
            business.TryGetProperty("sourceId", out _).Should().BeTrue();
            business.TryGetProperty("name", out _).Should().BeTrue();
        }
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-OFFERINGS-063")]
    public async Task GetBusinessOfferings_WithCrossProviderFilters_ReturnsStableMismatch()
    {
        var token = CatalogTestSupport.CreateToken(11);
        await using var factory = new CatalogApiFactory(devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(
                companyId == factory.FirstSourceCompanyId
                    ? CatalogTestSupport.CreateSnapshot(companyId, version: 1, companyNameAr: "الأول")
                    : CatalogTestSupport.CreateSnapshot(companyId, version: 1, companyNameAr: "الثاني"));
        using var client = factory.CreateApiClient();
        using var businessesRequest = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            deviceToken: token);

        using var businessesResponse = await client.SendAsync(businessesRequest);
        using var businessesDocument = await ReadJsonAsync(businessesResponse);
        var businesses = businessesDocument.RootElement.GetProperty("businesses").EnumerateArray().ToArray();
        var firstBusinessId = businesses[0].GetProperty("id").GetGuid();
        var foreignBranchId = businesses[1].GetProperty("branches").EnumerateArray().Single().GetProperty("id").GetGuid();

        using var categoriesRequest = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/categories",
            deviceToken: token);
        using var categoriesResponse = await client.SendAsync(categoriesRequest);
        using var categoriesDocument = await ReadJsonAsync(categoriesResponse);
        var categoryId = categoriesDocument.RootElement.GetProperty("categories")
            .EnumerateArray()
            .Single(category => category.GetProperty("business").GetProperty("id").GetGuid() == firstBusinessId)
            .GetProperty("id")
            .GetGuid();

        using var mismatchRequest = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{firstBusinessId:D}/offerings?categoryId={categoryId:D}&branchId={foreignBranchId:D}",
            deviceToken: token);
        using var response = await client.SendAsync(mismatchRequest);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString().Should().Be(CatalogProblemCodes.FilterMismatch);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-OFFERING-064")]
    public async Task GetOffering_WhenUnknown_ReturnsStableNotFound()
    {
        var token = CatalogTestSupport.CreateToken(12);
        await using var factory = new CatalogApiFactory(devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId, version: 1));
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/offerings/{Guid.NewGuid():D}",
            deviceToken: token,
            acceptLanguage: "he");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        document.RootElement.GetProperty("code").GetString().Should().Be(CatalogProblemCodes.OfferingNotFound);
        document.RootElement.GetProperty("language").GetString().Should().Be(ConfigurationLanguageResolver.Hebrew);
    }

    [Fact]
    public async Task Swagger_ContainsCatalogRoutes_AndCatalogServicesAreResolvable()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId, version: 1));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = await ReadJsonAsync(response);
        using var scope = factory.Services.CreateScope();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paths = document.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/v1/catalog/categories", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/catalog/businesses", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/catalog/businesses/{id}", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/catalog/businesses/{id}/offerings", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/catalog/offerings/{id}", out _).Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<ICatalogReadModelService>().Should().NotBeNull();
    }

    [Fact]
    public async Task Swagger_AndCatalogServicesResolve_WithSafeDefaultCatalogConfiguration()
    {
        await using var factory = new CatalogApiFactory(
            useActualBusinessApiClient: true,
            providers: Array.Empty<CatalogProviderRegistrationOptions>());
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = await ReadJsonAsync(response);
        using var scope = factory.Services.CreateScope();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("paths")
            .TryGetProperty("/api/v1/catalog/businesses", out _)
            .Should()
            .BeTrue();
        scope.ServiceProvider.GetRequiredService<ICatalogReadModelService>().Should().NotBeNull();
    }

    [Fact]
    public async Task GetBusinesses_WithNoConfiguredProviders_ReturnsIntentionalEmptyResponse()
    {
        var token = CatalogTestSupport.CreateToken(13);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)],
            providers: Array.Empty<CatalogProviderRegistrationOptions>(),
            useActualBusinessApiClient: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses?language=ar",
            deviceToken: token);

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
        document.RootElement.GetProperty("businesses").EnumerateArray().ToArray().Should().BeEmpty();
    }

    [Fact]
    public async Task GetCategories_WithNoConfiguredProviders_ReturnsIntentionalEmptyResponse()
    {
        var token = CatalogTestSupport.CreateToken(14);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)],
            providers: Array.Empty<CatalogProviderRegistrationOptions>(),
            useActualBusinessApiClient: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/categories?language=he",
            deviceToken: token);

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        document.RootElement.GetProperty("categories").EnumerateArray().ToArray().Should().BeEmpty();
    }

    [Fact]
    public async Task GetBusinesses_WhenBusinessApiClientConfigIsBlankAndRefreshIsRequired_ReturnsLocalizedUnavailableProblem()
    {
        var token = CatalogTestSupport.CreateToken(15);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)],
            useActualBusinessApiClient: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses?language=he",
            deviceToken: token);

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        document.RootElement.GetProperty("code").GetString().Should().Be(CatalogProblemCodes.Unavailable);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string? deviceToken = null,
        string? acceptLanguage = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (!string.IsNullOrWhiteSpace(deviceToken))
        {
            request.Headers.TryAddWithoutValidation(DeviceTokenDefaults.HeaderName, deviceToken);
        }

        if (!string.IsNullOrWhiteSpace(acceptLanguage))
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        return request;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload);
    }
}

public sealed class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly string _databaseName = $"CatalogApiTests-{Guid.NewGuid():N}";
    private readonly IEnumerable<CustomerDevice> _devices;
    private readonly IReadOnlyList<CatalogProviderRegistrationOptions> _providers;
    private readonly bool _useActualBusinessApiClient;

    public CatalogApiFactory(
        IEnumerable<CustomerDevice>? devices = null,
        IEnumerable<CatalogProviderRegistrationOptions>? providers = null,
        bool useActualBusinessApiClient = false)
    {
        _devices = devices ?? Array.Empty<CustomerDevice>();
        _providers = providers?.ToArray() ?? CreateDefaultProviders();
        _useActualBusinessApiClient = useActualBusinessApiClient;
    }

    public Guid FirstSourceCompanyId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public Guid SecondSourceCompanyId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal ScriptedBusinessApiClient BusinessApiClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:RemoteTest", "Server=(localdb)\\MSSQLLocalDB;Database=CatalogApiTests;Trusted_Connection=True;TrustServerCertificate=True;");
        builder.UseSetting("JwtSettings:SecretKey", "CatalogApiTestsSecret_Minimum32Chars");
        builder.UseSetting("JwtSettings:Issuer", "GhseeliApis.CatalogTests");
        builder.UseSetting("JwtSettings:Audience", "GhseeliApis.CatalogClients");
        builder.UseSetting("Swagger:Enabled", "true");
        builder.UseSetting("CatalogReadModel:FreshWindowSeconds", "300");
        builder.UseSetting("CatalogReadModel:MaxStaleWindowSeconds", "3600");
        builder.UseSetting("CatalogReadModel:LeaseDurationSeconds", "30");

        for (var index = 0; index < _providers.Count; index++)
        {
            builder.UseSetting(
                $"CatalogReadModel:Providers:{index}:SourceCompanyId",
                _providers[index].SourceCompanyId.ToString());
            builder.UseSetting(
                $"CatalogReadModel:Providers:{index}:Enabled",
                _providers[index].Enabled.ToString());
            builder.UseSetting(
                $"CatalogReadModel:Providers:{index}:Order",
                _providers[index].Order.ToString());
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            if (!_useActualBusinessApiClient)
            {
                services.RemoveAll<IBusinessApiClient>();
                services.AddSingleton<IBusinessApiClient>(_ => BusinessApiClient);
            }

            using var scope = services.BuildServiceProvider().CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
            if (_devices.Any())
            {
                context.CustomerDevices.AddRange(_devices);
                context.SaveChanges();
            }
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    public new async ValueTask DisposeAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.EnsureDeletedAsync();
        }
        catch
        {
        }

        await base.DisposeAsync();
    }

    private CatalogProviderRegistrationOptions[] CreateDefaultProviders() =>
    [
        new CatalogProviderRegistrationOptions
        {
            SourceCompanyId = FirstSourceCompanyId,
            Enabled = true,
            Order = 0
        },
        new CatalogProviderRegistrationOptions
        {
            SourceCompanyId = SecondSourceCompanyId,
            Enabled = true,
            Order = 1
        }
    ];
}
