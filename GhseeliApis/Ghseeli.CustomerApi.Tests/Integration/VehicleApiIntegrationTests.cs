using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.Tests.Support;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Verifies the saved vehicle HTTP contract through the real Customer API pipeline.
/// </summary>
public sealed class VehicleApiIntegrationTests
{
    [Fact]
    [Trait("ScenarioId", "FAN-VEHICLE-CRUD-001")]
    [Trait("ScenarioId", "FAN-VEHICLE-CRUD-002")]
    [Trait("ScenarioId", "FAN-VEHICLE-CRUD-003")]
    [Trait("ScenarioId", "FAN-VEHICLE-CRUD-015")]
    [Trait("ScenarioId", "FAN-VEHICLE-AUTH-017")]
    public async Task VehicleLifecycle_RoundTripsTypeAndImage_AndHidesForeignVehicle()
    {
        var ownerId = Guid.NewGuid();
        var foreignUserId = Guid.NewGuid();
        await using var factory = CreateFactory();
        await SeedUsersAsync(factory, (ownerId, false), (foreignUserId, false));
        using var owner = factory.CreateApiClient();
        owner.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt(ownerId));

        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/Vehicles")
        {
            Content = Json("""{"make":"Toyota","model":"Corolla","year":"2025","licensePlate":"TEST-001","color":"White","vehicleType":"Sedan","imageUrl":"https://cdn.example.test/vehicles/sedan.png"}""")
        };
        createRequest.Headers.TryAddWithoutValidation(
            "X-Correlation-Id",
            "corr-vehicle-create");
        using var create = await owner.SendAsync(createRequest);
        using var createJson = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var vehicleId = createJson.RootElement.GetProperty("id").GetGuid();

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        create.Headers.Location!.Scheme.Should().Be(Uri.UriSchemeHttps);
        create.Headers.Location.Host.Should().Be("localhost");
        create.Headers.Location.AbsolutePath.Should().Be($"/api/Vehicles/{vehicleId:D}");
        create.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        create.Content.Headers.ContentType.CharSet.Should().Be("utf-8");
        create.Headers.GetValues("X-Correlation-Id")
            .Should().ContainSingle("corr-vehicle-create");
        create.Headers.CacheControl!.NoStore.Should().BeTrue();
        create.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        create.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        create.Headers.GetValues("Referrer-Policy").Should().ContainSingle("no-referrer");
        create.Headers.GetValues("Content-Security-Policy")
            .Should().ContainSingle("default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
        create.Headers.GetValues("Permissions-Policy")
            .Should().ContainSingle("camera=(), microphone=(), geolocation=(), payment=()");
        create.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse();
        create.Headers.TryGetValues("Access-Control-Allow-Origin", out _).Should().BeFalse();
        createJson.RootElement.GetProperty("vehicleType").GetString().Should().Be("Sedan");
        createJson.RootElement.GetProperty("imageUrl").GetString()
            .Should().Be("https://cdn.example.test/vehicles/sedan.png");
        await AssertSingleProductionVehicleAsync(
            factory,
            vehicleId,
            "Sedan",
            "https://cdn.example.test/vehicles/sedan.png");

        using var list = await owner.GetAsync("/api/Vehicles/my-vehicles");
        using var listJson = await JsonDocument.ParseAsync(await list.Content.ReadAsStreamAsync());
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        listJson.RootElement.EnumerateArray().Should().ContainSingle();
        listJson.RootElement[0].GetProperty("vehicleType").GetString().Should().Be("Sedan");
        listJson.RootElement[0].GetProperty("imageUrl").GetString()
            .Should().Be("https://cdn.example.test/vehicles/sedan.png");

        using var detail = await owner.GetAsync($"/api/Vehicles/{vehicleId:D}");
        using var detailJson = await JsonDocument.ParseAsync(await detail.Content.ReadAsStreamAsync());
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        detailJson.RootElement.GetProperty("imageUrl").GetString()
            .Should().Be("https://cdn.example.test/vehicles/sedan.png");

        using var update = await owner.PutAsync(
            $"/api/Vehicles/{vehicleId:D}",
            Json("""{"make":"Toyota","model":"Corolla","year":"2025","licensePlate":"TEST-001","color":"Black","vehicleType":"Suv5Seater","imageUrl":"https://cdn.example.test/vehicles/suv.png"}"""));
        using var updateJson = await JsonDocument.ParseAsync(await update.Content.ReadAsStreamAsync());
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        updateJson.RootElement.GetProperty("vehicleType").GetString().Should().Be("Suv5Seater");
        updateJson.RootElement.GetProperty("imageUrl").GetString()
            .Should().Be("https://cdn.example.test/vehicles/suv.png");
        await AssertSingleProductionVehicleAsync(
            factory,
            vehicleId,
            "Suv5Seater",
            "https://cdn.example.test/vehicles/suv.png");

        using var foreign = factory.CreateApiClient();
        foreign.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt(foreignUserId));
        using var foreignRead = await foreign.GetAsync($"/api/Vehicles/{vehicleId:D}");
        foreignRead.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var foreignJson = await JsonDocument.ParseAsync(
            await foreignRead.Content.ReadAsStreamAsync());
        foreignJson.RootElement.EnumerateObject().Should().HaveCount(7);
        foreignJson.RootElement.GetProperty("type").GetString()
            .Should().Be("https://api.ghseeli.example/errors/resource_not_found");
        foreignJson.RootElement.GetProperty("title").GetString()
            .Should().Be("تعذر إكمال الطلب.");
        foreignJson.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        foreignJson.RootElement.GetProperty("detail").GetString()
            .Should().Be("المورد المطلوب غير موجود.");
        foreignJson.RootElement.GetProperty("code").GetString()
            .Should().Be("resource_not_found");
        foreignJson.RootElement.GetProperty("language").GetString().Should().Be("ar");
        foreignJson.RootElement.GetProperty("correlationId").GetString()
            .Should().Be(foreignRead.Headers.GetValues("X-Correlation-Id").Single());
        var foreignBody = foreignJson.RootElement.GetRawText().ToLowerInvariant();
        foreignBody.Should().NotContain(vehicleId.ToString().ToLowerInvariant());
        foreignBody.Should().NotContain(ownerId.ToString().ToLowerInvariant());
    }

    [Fact]
    [Trait("ScenarioId", "FAN-VEHICLE-CRUD-004")]
    [Trait("ScenarioId", "FAN-VEHICLE-CRUD-016")]
    public async Task Delete_RemovesVehicle_AndSubsequentReadIsNotFound()
    {
        var ownerId = Guid.NewGuid();
        await using var factory = CreateFactory();
        await SeedUsersAsync(factory, (ownerId, false));
        using var client = AuthenticatedClient(factory, ownerId);
        var vehicleId = await CreateVehicleAsync(client);

        using var delete = await client.DeleteAsync($"/api/Vehicles/{vehicleId:D}");
        using var read = await client.GetAsync($"/api/Vehicles/{vehicleId:D}");

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await delete.Content.ReadAsStringAsync()).Should().BeEmpty();
        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Vehicles.IgnoreQueryFilters().AnyAsync(value => value.Id == vehicleId))
            .Should().BeFalse();
    }

    [Fact]
    [Trait("ScenarioId", "FAN-VEHICLE-AUTH-005")]
    public async Task VehicleRoutes_RejectAnonymousRequests()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/Vehicles/my-vehicles");
        request.Headers.TryAddWithoutValidation(
            "X-Correlation-Id",
            "corr-vehicle-anonymous");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Correlation-Id")
            .Should().ContainSingle("corr-vehicle-anonymous");
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        var body = (await response.Content.ReadAsStringAsync()).ToLowerInvariant();
        body.Should().NotContain("token");
        body.Should().NotContain("exception");
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ForeignOwnerMutation_ReturnsNotFound_AndDoesNotMutate(string method)
    {
        var ownerId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        await using var factory = CreateFactory();
        await SeedUsersAsync(factory, (ownerId, false), (foreignId, false));
        using var owner = AuthenticatedClient(factory, ownerId);
        using var foreign = AuthenticatedClient(factory, foreignId);
        var vehicleId = await CreateVehicleAsync(owner);

        using var response = method == "PUT"
            ? await foreign.PutAsync(
                $"/api/Vehicles/{vehicleId:D}",
                Json(ValidVehicleJson("Suv7Seater", null, "Changed")))
            : await foreign.DeleteAsync($"/api/Vehicles/{vehicleId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var read = await owner.GetAsync($"/api/Vehicles/{vehicleId:D}");
        using var document = await JsonDocument.ParseAsync(await read.Content.ReadAsStreamAsync());
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("vehicleType").GetString().Should().Be("Sedan");
        document.RootElement.GetProperty("color").GetString().Should().Be("White");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-VEHICLE-PARTITION-008")]
    [Trait("ScenarioId", "FAN-VEHICLE-PARTITION-025")]
    public async Task DemoVehicle_IsInvisibleFromProductionPartition()
    {
        var demoUserId = Guid.NewGuid();
        await using var factory = CreateFactory();
        await SeedUsersAsync(factory, (demoUserId, true));
        using var demo = AuthenticatedClient(factory, demoUserId, DataPartitionNames.Demo);
        var vehicleId = await CreateVehicleAsync(demo);

        using var production = AuthenticatedClient(
            factory,
            demoUserId,
            DataPartitionNames.Production);
        using var response = await production.GetAsync($"/api/Vehicles/{vehicleId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await context.Vehicles
            .IgnoreQueryFilters()
            .Include(value => value.Owner)
            .SingleAsync(value => value.Id == vehicleId);
        stored.Owner.IsDemo.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("null")]
    [InlineData("\"Car\"")]
    [InlineData("\"sedan\"")]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-006")]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-018")]
    public async Task Create_RejectsNumericLegacyAndIncorrectCaseVehicleTypes(string vehicleType)
    {
        var userId = Guid.NewGuid();
        await using var factory = CreateFactory();
        await SeedUsersAsync(factory, (userId, false));
        using var client = AuthenticatedClient(factory, userId);
        (await CountVehiclesAsync(factory)).Should().Be(0);

        var body = string.IsNullOrEmpty(vehicleType)
            ? """{"imageUrl":null}"""
            : $"{{\"vehicleType\":{vehicleType},\"imageUrl\":null}}";
        using var response = await client.PostAsync(
            "/api/Vehicles",
            Json(body));
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString().Should().Be("vehicle_type_invalid");
        (await CountVehiclesAsync(factory)).Should().Be(0,
            "request contract validation must complete before persistence");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-027")]
    public async Task Create_RejectsExactly501CharacterImage_WithoutPersistence()
    {
        var userId = Guid.NewGuid();
        await using var factory = CreateFactory();
        await SeedUsersAsync(factory, (userId, false));
        using var client = AuthenticatedClient(factory, userId);
        var imageUrl = ExactLengthImageUrl(501);

        using var response = await client.PostAsync(
            "/api/Vehicles",
            Json(JsonSerializer.Serialize(new
            {
                vehicleType = "Sedan",
                imageUrl
            })));
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be("vehicle_image_url_invalid");
        (await CountVehiclesAsync(factory)).Should().Be(0,
            "image validation must complete before persistence");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-026")]
    public async Task Create_AcceptsExactly500CharacterImage_AndPersistsIt()
    {
        var userId = Guid.NewGuid();
        await using var factory = CreateFactory();
        await SeedUsersAsync(factory, (userId, false));
        using var client = AuthenticatedClient(factory, userId);
        var imageUrl = ExactLengthImageUrl(500);

        using var response = await client.PostAsync(
            "/api/Vehicles",
            Json(ValidVehicleJson("Sedan", imageUrl)));
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        document.RootElement.GetProperty("imageUrl").GetString().Should().Be(imageUrl);
        var vehicleId = document.RootElement.GetProperty("id").GetGuid();
        await AssertSingleProductionVehicleAsync(factory, vehicleId, "Sedan", imageUrl);
    }

    [Theory]
    [InlineData("http://cdn.example.test/vehicle.png")]
    [InlineData("/images/vehicle.png")]
    [InlineData("https://user:password@cdn.example.test/vehicle.png")]
    [InlineData("not-a-url")]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-007")]
    public async Task Create_RejectsInvalidVehicleImageUrls(string imageUrl)
    {
        var userId = Guid.NewGuid();
        await using var factory = CreateFactory();
        await SeedUsersAsync(factory, (userId, false));
        using var client = AuthenticatedClient(factory, userId);
        (await CountVehiclesAsync(factory)).Should().Be(0);

        using var response = await client.PostAsync(
            "/api/Vehicles",
            Json(JsonSerializer.Serialize(new
            {
                vehicleType = "Sedan",
                imageUrl
            })));
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be("vehicle_image_url_invalid");
        (await CountVehiclesAsync(factory)).Should().Be(0,
            "image validation must complete before persistence");
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("\"Car\"", null)]
    [InlineData("\"sedan\"", null)]
    [InlineData("\"Sedan\"", "/images/vehicle.png")]
    [InlineData("\"Sedan\"", "https://user:password@cdn.example.test/vehicle.png")]
    [InlineData("\"Sedan\"", "not-a-url")]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-019")]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-020")]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-021")]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-022")]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-023")]
    public async Task Update_RejectsInvalidVehicleContract_AndPreservesRow(
        string vehicleType,
        string? imageUrl)
    {
        var ownerId = Guid.NewGuid();
        await using var factory = CreateFactory();
        await SeedUsersAsync(factory, (ownerId, false));
        using var client = AuthenticatedClient(factory, ownerId);
        var vehicleId = await CreateVehicleAsync(client);
        var imageJson = imageUrl is null ? "null" : JsonSerializer.Serialize(imageUrl);

        using var update = await client.PutAsync(
            $"/api/Vehicles/{vehicleId:D}",
            Json($"{{\"make\":\"Changed\",\"model\":\"Changed\",\"year\":\"2025\",\"licensePlate\":\"CHANGED\",\"color\":\"Changed\",\"vehicleType\":{vehicleType},\"imageUrl\":{imageJson}}}"));
        using var read = await client.GetAsync($"/api/Vehicles/{vehicleId:D}");
        using var document = await JsonDocument.ParseAsync(await read.Content.ReadAsStreamAsync());

        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("make").GetString().Should().Be("Toyota");
        document.RootElement.GetProperty("vehicleType").GetString().Should().Be("Sedan");
        document.RootElement.GetProperty("imageUrl").GetString()
            .Should().Be("https://cdn.example.test/vehicles/sedan.png");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-024")]
    [Trait("ScenarioId", "FAN-VEHICLE-VALIDATION-028")]
    public async Task Update_RejectsExactly501CharacterImage_AndAcceptsExactly500()
    {
        var ownerId = Guid.NewGuid();
        await using var factory = CreateFactory();
        await SeedUsersAsync(factory, (ownerId, false));
        using var client = AuthenticatedClient(factory, ownerId);
        var vehicleId = await CreateVehicleAsync(client);
        var fiveHundred = ExactLengthImageUrl(500);
        var fiveHundredOne = ExactLengthImageUrl(501);

        using var rejected = await client.PutAsync(
            $"/api/Vehicles/{vehicleId:D}",
            Json(ValidVehicleJson("Sedan", fiveHundredOne)));
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertSingleProductionVehicleAsync(
            factory,
            vehicleId,
            "Sedan",
            "https://cdn.example.test/vehicles/sedan.png");

        using var accepted = await client.PutAsync(
            $"/api/Vehicles/{vehicleId:D}",
            Json(ValidVehicleJson("Sedan", fiveHundred)));
        using var document = await JsonDocument.ParseAsync(
            await accepted.Content.ReadAsStreamAsync());

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("imageUrl").GetString().Should().Be(fiveHundred);
        await AssertSingleProductionVehicleAsync(
            factory,
            vehicleId,
            "Sedan",
            fiveHundred);
    }

    private static Step15CustomerApiFactory CreateFactory() =>
        new(
            CatalogTestSupport.CreateSnapshot(
                Guid.NewGuid(),
                version: 1,
                companyNameAr: "شركة الاختبار",
                companyNameHe: null),
            []);

    private static StringContent Json(string value) =>
        new(value, Encoding.UTF8, "application/json");

    private static HttpClient AuthenticatedClient(
        Step15CustomerApiFactory factory,
        Guid userId,
        string partition = DataPartitionNames.Production)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt(userId, partition));
        return client;
    }

    private static async Task<Guid> CreateVehicleAsync(HttpClient client)
    {
        using var response = await client.PostAsync(
            "/api/Vehicles",
            Json(ValidVehicleJson(
                "Sedan",
                "https://cdn.example.test/vehicles/sedan.png")));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static string ValidVehicleJson(
        string vehicleType,
        string? imageUrl,
        string color = "White") =>
        JsonSerializer.Serialize(new
        {
            make = "Toyota",
            model = "Corolla",
            year = "2025",
            licensePlate = "TEST-001",
            color,
            vehicleType,
            imageUrl
        });

    private static string ExactLengthImageUrl(int length)
    {
        const string prefix = "https://cdn.example.test/";
        return prefix + new string('a', length - prefix.Length);
    }

    private static async Task<int> CountVehiclesAsync(Step15CustomerApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Vehicles.IgnoreQueryFilters().CountAsync();
    }

    private static async Task AssertSingleProductionVehicleAsync(
        Step15CustomerApiFactory factory,
        Guid vehicleId,
        string expectedType,
        string? expectedImageUrl)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.ChangeTracker.Clear();
        var stored = await context.Vehicles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(value => value.Owner)
            .SingleAsync();
        stored.Id.Should().Be(vehicleId);
        stored.Owner.IsDemo.Should().BeFalse();
        stored.VehicleType.ToString().Should().Be(expectedType);
        stored.ImageUrl.Should().Be(expectedImageUrl);
    }

    private static async Task SeedUsersAsync(
        Step15CustomerApiFactory factory,
        params (Guid UserId, bool IsDemo)[] users)
    {
        foreach (var partitionGroup in users.GroupBy(value => value.IsDemo))
        {
            using var scope = factory.Services.CreateScope();
            scope.ServiceProvider
                .GetRequiredService<GhseeliApis.DataPartitioning.ICustomerDataPartitionContext>()
                .SetTrustedPartition(
                    partitionGroup.Key
                        ? DataPartitionNames.Demo
                        : DataPartitionNames.Production);
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Users.AddRange(partitionGroup.Select((value, index) => new User
            {
                Id = value.UserId,
                FullName = $"Vehicle User {index}",
                Email = $"vehicle-user-{value.UserId:N}@example.test",
                UserName = $"vehicle-user-{value.UserId:N}@example.test",
                NormalizedEmail = $"VEHICLE-USER-{value.UserId:N}@EXAMPLE.TEST".ToUpperInvariant(),
                NormalizedUserName = $"VEHICLE-USER-{value.UserId:N}@EXAMPLE.TEST".ToUpperInvariant(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }));
            await context.SaveChangesAsync();
        }
    }

    private static string Jwt(
        Guid userId,
        string partition = DataPartitionNames.Production)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("CheckoutDraftApiTestsSecret_Minimum32Chars")),
            SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "GhseeliApis.CheckoutDraftTests",
            audience: "GhseeliApis.CheckoutDraftClients",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "User"),
                new Claim(DataPartitionNames.ClaimType, partition)
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials));
    }
}
