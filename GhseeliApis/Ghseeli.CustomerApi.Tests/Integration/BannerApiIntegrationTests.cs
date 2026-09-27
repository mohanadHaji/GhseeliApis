using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.DataPartitioning;
using GhseeliApis.DTOs.Banners;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Banners;
using GhseeliApis.Services.Devices;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Exercises public and Customer Admin banner routes through the real HTTP pipeline.
/// </summary>
public sealed class BannerApiIntegrationTests
{
    [Fact]
    [Trait("ScenarioId", "FAN-BANNER-PUBLIC-001")]
    [Trait("ScenarioId", "FAN-OPTIONAL-READS-004")]
    public async Task Public_list_is_anonymous_active_deterministic_and_has_no_admin_fields()
    {
        await using var factory = new CatalogApiFactory(providers: []);
        await SeedAsync(factory,
            BannerAt("00000000-0000-0000-0000-000000000003", 10, true),
            BannerAt("00000000-0000-0000-0000-000000000001", 10, true),
            BannerAt("00000000-0000-0000-0000-000000000002", 1, false));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/api/v1/banners?language=he");
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var banners = json.RootElement.GetProperty("banners");
        banners.GetArrayLength().Should().Be(2);
        banners[0].GetProperty("id").GetGuid().Should().Be(
            Guid.Parse("00000000-0000-0000-0000-000000000001"));
        banners[1].GetProperty("id").GetGuid().Should().Be(
            Guid.Parse("00000000-0000-0000-0000-000000000003"));
        banners[0].GetRawText().Should().NotContainAny(
            "isActive", "createdAtUtc", "updatedAtUtc", "rowVersion", "isDemo");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BANNER-PUBLIC-003")]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-034")]
    public async Task Public_list_rejects_malformed_optional_device()
    {
        await using var factory = new CatalogApiFactory(providers: []);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-Device-Token", "invalid");

        using var response = await client.GetAsync("/api/v1/banners");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(response)).Should().Be("device_token_invalid");
    }

    [Theory]
    [InlineData("unknown", "device_token_invalid")]
    [InlineData("disabled", "device_token_inactive")]
    [InlineData("expired", "device_token_expired")]
    [InlineData("rotated", "device_token_invalid")]
    public async Task Optional_device_rejects_unknown_disabled_expired_and_rotated_tokens(
        string state,
        string expectedCode)
    {
        var suppliedToken = CatalogTestSupport.CreateToken(211);
        var storedToken = state == "rotated"
            ? CatalogTestSupport.CreateToken(212)
            : suppliedToken;
        var devices = state == "unknown"
            ? []
            : new[] { CatalogTestSupport.CreateDevice(storedToken) };
        if (devices.Length == 1)
        {
            devices[0].IsActive = state != "disabled";
            devices[0].ExpiresAt = state == "expired"
                ? DateTimeOffset.UtcNow.AddMinutes(-1)
                : DateTimeOffset.UtcNow.AddDays(1);
        }

        await using var factory = new CatalogApiFactory(devices: devices, providers: []);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-Device-Token", suppliedToken);

        using var response = await client.GetAsync("/api/v1/banners");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(response)).Should().Be(expectedCode);
    }

    [Fact]
    public async Task Optional_device_rejects_duplicate_header_values()
    {
        await using var factory = new CatalogApiFactory(providers: []);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Device-Token",
            new[] { CatalogTestSupport.CreateToken(213), CatalogTestSupport.CreateToken(214) });

        using var response = await client.GetAsync("/api/v1/banners");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(response)).Should().Be("device_token_invalid");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-DEMO-021")]
    [Trait("ScenarioId", "FAN-BANNER-PARTITION-020")]
    public async Task Optional_demo_device_returns_only_demo_banners()
    {
        var token = CatalogTestSupport.CreateToken(201);
        await using var factory = new CatalogApiFactory(providers: []);
        await SeedAsync(factory, BannerAt(Guid.NewGuid().ToString(), 1, true));
        await SeedDemoAsync(
            factory,
            CatalogTestSupport.CreateDevice(token),
            BannerAt(Guid.NewGuid().ToString(), 1, true, "demo"));
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-Device-Token", token);

        using var response = await client.GetAsync("/api/v1/banners");
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.RootElement.GetProperty("banners").GetArrayLength().Should().Be(1);
        json.RootElement.GetProperty("banners")[0].GetProperty("imageUrl").GetString()
            .Should().Contain("demo");
    }

    [Fact]
    public async Task Production_device_sees_only_production_when_ids_and_order_collide()
    {
        var token = CatalogTestSupport.CreateToken(215);
        var production = CatalogTestSupport.CreateDevice(token);
        await using var factory = new CatalogApiFactory(
            devices: [production],
            providers: []);
        await SeedAsync(factory, BannerAt(Guid.NewGuid().ToString(), 5, true, "production"));
        await SeedDemoAsync(
            factory,
            CatalogTestSupport.CreateDevice(CatalogTestSupport.CreateToken(216)),
            BannerAt(Guid.NewGuid().ToString(), 5, true, "demo"));
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-Device-Token", token);

        using var response = await client.GetAsync("/api/v1/banners");
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.RootElement.GetProperty("banners").GetArrayLength().Should().Be(1);
        json.RootElement.GetProperty("banners")[0].GetProperty("imageUrl").GetString()
            .Should().EndWith("/production.png");
    }

    [Fact]
    public async Task Jwt_and_optional_device_partition_mismatch_is_forbidden()
    {
        var token = CatalogTestSupport.CreateToken(217);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)],
            providers: []);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("User", partition: DataPartitionNames.Demo));
        client.DefaultRequestHeaders.Add("X-Device-Token", token);

        using var response = await client.GetAsync("/api/v1/banners");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(response)).Should().Be("data_partition_mismatch");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BANNER-ADMIN-004")]
    public async Task Admin_can_create_list_update_and_delete_while_public_visibility_tracks_active_state()
    {
        await using var factory = new CatalogApiFactory(providers: []);
        var id = Guid.NewGuid();
        var version = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await SeedAsync(factory, new Banner
        {
            Id = id,
            ImageUrl = "https://cdn.example.test/banners/original.png",
            DisplayOrder = 1,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            RowVersion = version
        });
        using var admin = factory.CreateApiClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("Admin"));

        using var create = await admin.PostAsJsonAsync(
            "/api/v1/admin/banners",
            new
            {
                imageUrl = "https://cdn.example.test/banners/new.png",
                displayOrder = 2,
                isActive = true
            });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        using var list = await admin.GetAsync("/api/v1/admin/banners");
        using var listJson = await JsonDocument.ParseAsync(
            await list.Content.ReadAsStreamAsync());
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminItem = listJson.RootElement.GetProperty("banners").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == id);
        adminItem.GetProperty("isActive").GetBoolean().Should().BeTrue();
        adminItem.GetProperty("rowVersion").GetString().Should().NotBeNull();

        using var update = await admin.PutAsJsonAsync(
            $"/api/v1/admin/banners/{id:D}",
            new
            {
                imageUrl = "https://cdn.example.test/banners/updated.png",
                displayOrder = 8,
                isActive = false,
                expectedRowVersion = Convert.ToBase64String(version)
            });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        using var publicResponse = await admin.GetAsync("/api/v1/banners");
        using var publicJson = await JsonDocument.ParseAsync(
            await publicResponse.Content.ReadAsStreamAsync());
        publicJson.RootElement.GetProperty("banners").EnumerateArray()
            .Should().NotContain(item => item.GetProperty("id").GetGuid() == id);

        using var delete = await admin.DeleteAsync(
            $"/api/v1/admin/banners/{id:D}?expectedRowVersion=" +
            Uri.EscapeDataString(Convert.ToBase64String(version)));
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var adminAfterDelete = await admin.GetAsync("/api/v1/admin/banners");
        using var adminAfterDeleteJson = await JsonDocument.ParseAsync(
            await adminAfterDelete.Content.ReadAsStreamAsync());
        adminAfterDeleteJson.RootElement.GetProperty("banners").EnumerateArray()
            .Should().NotContain(item => item.GetProperty("id").GetGuid() == id);
        using var publicAfterDelete = await admin.GetAsync("/api/v1/banners");
        using var publicAfterDeleteJson = await JsonDocument.ParseAsync(
            await publicAfterDelete.Content.ReadAsStreamAsync());
        publicAfterDeleteJson.RootElement.GetProperty("banners").EnumerateArray()
            .Should().NotContain(item => item.GetProperty("id").GetGuid() == id);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized, "customer_authentication_required")]
    [InlineData("User", HttpStatusCode.Forbidden, "customer_authorization_forbidden")]
    [InlineData("Company", HttpStatusCode.Forbidden, "customer_authorization_forbidden")]
    public async Task Admin_routes_reject_missing_and_non_admin_customer_identity(
        string? role,
        HttpStatusCode status,
        string code)
    {
        await using var factory = new CatalogApiFactory(providers: []);
        using var client = factory.CreateApiClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Jwt(role));
        }

        using var response = await client.GetAsync("/api/v1/admin/banners");

        response.StatusCode.Should().Be(status);
        (await ProblemCodeAsync(response)).Should().Be(code);
    }

    [Fact]
    public async Task Admin_mutations_reject_user_company_and_business_tokens_without_changes()
    {
        var id = Guid.NewGuid();
        var version = new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 };
        await using var factory = new CatalogApiFactory(providers: []);
        await SeedAsync(factory, new Banner
        {
            Id = id,
            ImageUrl = "https://cdn.example.test/banners/protected.png",
            DisplayOrder = 4,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            RowVersion = version
        });

        using (var user = factory.CreateApiClient())
        {
            user.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Jwt("User"));
            using var response = await user.PostAsJsonAsync(
                "/api/v1/admin/banners",
                new { imageUrl = "https://cdn.example.test/banners/no.png", displayOrder = 1, isActive = true });
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var company = factory.CreateApiClient())
        {
            company.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Jwt("Company"));
            using var response = await company.PutAsJsonAsync(
                $"/api/v1/admin/banners/{id:D}",
                new
                {
                    imageUrl = "https://cdn.example.test/banners/no.png",
                    displayOrder = 5,
                    isActive = false,
                    expectedRowVersion = Convert.ToBase64String(version)
                });
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var business = factory.CreateApiClient())
        {
            business.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    Jwt("Admin", "BusinessApiTestsSecret_Minimum32Chars"));
            using var response = await business.DeleteAsync(
                $"/api/v1/admin/banners/{id:D}?expectedRowVersion=" +
                Uri.EscapeDataString(Convert.ToBase64String(version)));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var banner = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Banners.SingleAsync(value => value.Id == id);
        banner.DisplayOrder.Should().Be(4);
        banner.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Stale_update_and_delete_return_version_conflict_and_preserve_row()
    {
        var id = Guid.NewGuid();
        var current = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 };
        await using var factory = new CatalogApiFactory(providers: []);
        await SeedAsync(factory, new Banner
        {
            Id = id,
            ImageUrl = "https://cdn.example.test/banners/current.png",
            DisplayOrder = 6,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            RowVersion = current
        });
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("Admin"));
        var stale = Convert.ToBase64String(new byte[] { 2, 2, 2, 2, 2, 2, 2, 2 });

        using var update = await client.PutAsJsonAsync(
            $"/api/v1/admin/banners/{id:D}",
            new
            {
                imageUrl = "https://cdn.example.test/banners/stale.png",
                displayOrder = 7,
                isActive = false,
                expectedRowVersion = stale
            });
        using var delete = await client.DeleteAsync(
            $"/api/v1/admin/banners/{id:D}?expectedRowVersion={Uri.EscapeDataString(stale)}");

        update.StatusCode.Should().Be(HttpStatusCode.Conflict);
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(update)).Should().Be("banner_version_conflict");
        (await ProblemCodeAsync(delete)).Should().Be("banner_version_conflict");
        await using var scope = factory.Services.CreateAsyncScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Banners.SingleAsync(value => value.Id == id);
        persisted.ImageUrl.Should().EndWith("current.png");
        persisted.DisplayOrder.Should().Be(6);
        persisted.IsActive.Should().BeTrue();
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BANNER-VALIDATION-009")]
    public async Task Admin_validation_returns_localized_problem_and_field_errors_without_mutation()
    {
        await using var factory = new CatalogApiFactory(providers: []);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("Admin"));

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/banners?language=he",
            new { imageUrl = "http://unsafe.example.test/banner.png", displayOrder = 10001, isActive = true });
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.RootElement.GetProperty("code").GetString().Should().Be("banner_invalid");
        json.RootElement.GetProperty("language").GetString().Should().Be("he");
        json.RootElement.GetProperty("fieldErrors").TryGetProperty("imageUrl", out _)
            .Should().BeTrue();
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Banners.IgnoreQueryFilters().CountAsync())
            .Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BANNER-VALIDATION-018")]
    public async Task Admin_update_rejects_display_order_overflow_and_preserves_every_persisted_field()
    {
        var id = Guid.NewGuid();
        var rowVersion = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 };
        await using var factory = new CatalogApiFactory(providers: []);
        await SeedAsync(factory, new Banner
        {
            Id = id,
            ImageUrl = "https://cdn.example.test/banners/original.png",
            DisplayOrder = 17,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            RowVersion = rowVersion
        });
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("Admin"));
        var expectedRowVersion = Convert.ToBase64String(rowVersion);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await context.Banners.SingleAsync(value => value.Id == id);
        var originalImageUrl = persisted.ImageUrl;
        var originalDisplayOrder = persisted.DisplayOrder;
        var originalIsActive = persisted.IsActive;
        var originalCreatedAtUtc = persisted.CreatedAtUtc;
        var originalUpdatedAtUtc = persisted.UpdatedAtUtc;
        var originalIsDemo = persisted.IsDemo;
        var originalRowVersion = persisted.RowVersion.ToArray();

        using var update = await client.PutAsJsonAsync(
            $"/api/v1/admin/banners/{id:D}",
            new
            {
                imageUrl = "https://cdn.example.test/banners/changed.png",
                displayOrder = 10001,
                isActive = false,
                expectedRowVersion
            });
        using var updateJson = await JsonDocument.ParseAsync(
            await update.Content.ReadAsStreamAsync());

        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        updateJson.RootElement.GetProperty("code").GetString().Should().Be("banner_invalid");
        updateJson.RootElement.GetProperty("fieldErrors")
            .TryGetProperty("displayOrder", out _)
            .Should().BeTrue();

        await context.Entry(persisted).ReloadAsync();
        persisted.Id.Should().Be(id);
        persisted.ImageUrl.Should().Be(originalImageUrl);
        persisted.DisplayOrder.Should().Be(originalDisplayOrder);
        persisted.IsActive.Should().Be(originalIsActive);
        persisted.CreatedAtUtc.Should().Be(originalCreatedAtUtc);
        persisted.UpdatedAtUtc.Should().Be(originalUpdatedAtUtc);
        persisted.IsDemo.Should().Be(originalIsDemo);
        persisted.RowVersion.Should().Equal(originalRowVersion);
    }

    [Fact]
    public async Task Admin_http_accepts_url_and_order_boundaries_and_rejects_overflow_without_extra_row()
    {
        const string prefix = "https://cdn.example.test/";
        var exactUrl = prefix + new string('a', 500 - prefix.Length);
        var tooLongUrl = prefix + new string('a', 501 - prefix.Length);
        await using var factory = new CatalogApiFactory(providers: []);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("Admin"));

        using var zero = await client.PostAsJsonAsync(
            "/api/v1/admin/banners",
            new { imageUrl = exactUrl, displayOrder = 0, isActive = true });
        using var maximum = await client.PostAsJsonAsync(
            "/api/v1/admin/banners",
            new
            {
                imageUrl = "https://cdn.example.test/banners/maximum.png",
                displayOrder = 10000,
                isActive = true
            });
        using var tooLong = await client.PostAsJsonAsync(
            "/api/v1/admin/banners",
            new { imageUrl = tooLongUrl, displayOrder = 1, isActive = true });
        using var overflow = await client.PostAsJsonAsync(
            "/api/v1/admin/banners",
            new
            {
                imageUrl = "https://cdn.example.test/banners/overflow.png",
                displayOrder = 10001,
                isActive = true
            });

        zero.StatusCode.Should().Be(HttpStatusCode.Created);
        maximum.StatusCode.Should().Be(HttpStatusCode.Created);
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        overflow.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Banners.CountAsync())
            .Should().Be(2);
    }

    [Theory]
    [InlineData("https://user:password@cdn.example.test/banner.png")]
    [InlineData("https:///banner.png")]
    [InlineData("https://")]
    public async Task Admin_http_rejects_url_credentials_and_missing_or_empty_host(
        string imageUrl)
    {
        await using var factory = new CatalogApiFactory(providers: []);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("Admin"));

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/banners",
            new { imageUrl, displayOrder = 1, isActive = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).Should().Be("banner_invalid");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Banners.CountAsync())
            .Should().Be(0);
    }

    [Fact]
    public async Task Admin_update_and_delete_require_well_formed_rowversion_without_mutation()
    {
        var id = Guid.NewGuid();
        var version = new byte[8];
        await using var factory = new CatalogApiFactory(providers: []);
        await SeedAsync(factory, new Banner
        {
            Id = id,
            ImageUrl = "https://cdn.example.test/banners/original.png",
            DisplayOrder = 3,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            RowVersion = version
        });
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("Admin"));

        foreach (var body in new[]
                 {
                     """{"imageUrl":"https://cdn.example.test/banners/no.png","displayOrder":4,"isActive":false}""",
                     """{"imageUrl":"https://cdn.example.test/banners/no.png","displayOrder":4,"isActive":false,"expectedRowVersion":"not-base64"}"""
                 })
        {
            using var update = await client.PutAsync(
                $"/api/v1/admin/banners/{id:D}",
                new StringContent(body, Encoding.UTF8, "application/json"));
            update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ProblemCodeAsync(update)).Should().Be("banner_invalid");
        }

        foreach (var query in new[] { string.Empty, "?expectedRowVersion=not-base64" })
        {
            using var delete = await client.DeleteAsync(
                $"/api/v1/admin/banners/{id:D}{query}");
            delete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ProblemCodeAsync(delete)).Should().Be("banner_invalid");
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Banners.SingleAsync(value => value.Id == id);
        persisted.ImageUrl.Should().EndWith("/original.png");
        persisted.DisplayOrder.Should().Be(3);
        persisted.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Admin_body_boundaries_return_stable_400_415_and_413_without_mutation()
    {
        await using var factory = new CatalogApiFactory(providers: []);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("Admin"));

        using var empty = await client.PostAsync(
            "/api/v1/admin/banners",
            new ByteArrayContent([]));
        using var nullBody = await client.PostAsync(
            "/api/v1/admin/banners",
            new StringContent("null", Encoding.UTF8, "application/json"));
        using var malformed = await client.PostAsync(
            "/api/v1/admin/banners",
            new StringContent("{", Encoding.UTF8, "application/json"));
        using var unsupported = await client.PostAsync(
            "/api/v1/admin/banners",
            new StringContent("{}", Encoding.UTF8, "text/plain"));
        using var oversized = await client.PostAsync(
            "/api/v1/admin/banners",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    imageUrl = "https://cdn.example.test/" + new string('a', 70_000),
                    displayOrder = 1,
                    isActive = true
                }),
                Encoding.UTF8,
                "application/json"));

        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        nullBody.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unsupported.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        oversized.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Banners.CountAsync())
            .Should().Be(0);
    }

    [Fact]
    public async Task Banner_problem_is_localized_with_content_language_title_detail_and_correlation()
    {
        await using var factory = new CatalogApiFactory(providers: []);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("Admin"));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/admin/banners?language=he")
        {
            Content = JsonContent.Create(new
            {
                imageUrl = "http://unsafe.example.test/banner.png",
                displayOrder = 1,
                isActive = true
            })
        };
        request.Headers.Add("X-Correlation-ID", "banner-localization-correlation");

        using var response = await client.SendAsync(request);
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentLanguage.Should().ContainSingle("he");
        json.RootElement.GetProperty("title").GetString().Should().Contain("באנר");
        json.RootElement.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("language").GetString().Should().Be("he");
        json.RootElement.GetProperty("correlationId").GetString()
            .Should().Be("banner-localization-correlation");
    }

    [Fact]
    public async Task Nonexistent_and_cross_partition_mutations_follow_frozen_non_disclosure_contract()
    {
        var demoId = Guid.NewGuid();
        var version = new byte[8];
        await using var factory = new CatalogApiFactory(providers: []);
        await SeedDemoAsync(
            factory,
            CatalogTestSupport.CreateDevice(CatalogTestSupport.CreateToken(218)),
            new Banner
            {
                Id = demoId,
                ImageUrl = "https://cdn.example.test/banners/demo-private.png",
                DisplayOrder = 1,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                RowVersion = version
            });
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("Admin"));
        var missingId = Guid.NewGuid();

        foreach (var id in new[] { missingId, demoId })
        {
            using var update = await client.PutAsJsonAsync(
                $"/api/v1/admin/banners/{id:D}",
                new
                {
                    imageUrl = "https://cdn.example.test/banners/no.png",
                    displayOrder = 2,
                    isActive = false,
                    expectedRowVersion = Convert.ToBase64String(version)
                });
            using var delete = await client.DeleteAsync(
                $"/api/v1/admin/banners/{id:D}?expectedRowVersion=" +
                Uri.EscapeDataString(Convert.ToBase64String(version)));

            update.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await ProblemCodeAsync(update)).Should().Be("banner_not_found");
            delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(delete)).Should().Be("banner_version_conflict");
        }

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICustomerDataPartitionContext>()
            .SetTrustedPartition(DataPartitionNames.Demo);
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Banners.SingleAsync(value => value.Id == demoId))
            .ImageUrl.Should().EndWith("/demo-private.png");
    }

    [Fact]
    public async Task Inactive_banner_is_admin_visible_and_public_hidden()
    {
        var id = Guid.NewGuid();
        await using var factory = new CatalogApiFactory(providers: []);
        await SeedAsync(factory, BannerAt(id.ToString(), 1, false, "inactive"));
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jwt("Admin"));

        using var admin = await client.GetAsync("/api/v1/admin/banners");
        using var adminJson = await JsonDocument.ParseAsync(
            await admin.Content.ReadAsStreamAsync());
        using var publicResponse = await client.GetAsync("/api/v1/banners");
        using var publicJson = await JsonDocument.ParseAsync(
            await publicResponse.Content.ReadAsStreamAsync());

        adminJson.RootElement.GetProperty("banners").EnumerateArray()
            .Should().Contain(value => value.GetProperty("id").GetGuid() == id);
        publicJson.RootElement.GetProperty("banners").EnumerateArray()
            .Should().NotContain(value => value.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Induced_banner_failure_returns_safe_localized_500_with_security_headers_and_no_leakage()
    {
        await using var factory = new CatalogApiFactory(providers: []);
        await using var failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBannerService>();
                services.AddScoped<IBannerService, ThrowingBannerService>();
            }));
        using var client = failingFactory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/banners?language=ar");
        request.Headers.Add("X-Correlation-ID", "banner-safe-500");
        request.Headers.Add("X-Secret-Probe", "do-not-leak-this-value");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        response.Content.Headers.ContentLanguage.Should().ContainSingle("ar");
        json.RootElement.GetProperty("code").GetString().Should().Be("unexpected_error");
        json.RootElement.GetProperty("correlationId").GetString().Should().Be("banner-safe-500");
        json.RootElement.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        body.Should().NotContain("banner-database-secret");
        body.Should().NotContain("do-not-leak-this-value");
        body.Should().NotContain("System.");
    }

    [Fact]
    public async Task Swagger_exposes_public_optional_device_and_admin_bearer_contract()
    {
        await using var factory = new CatalogApiFactory(providers: []);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paths = json.RootElement.GetProperty("paths");
        paths.GetProperty("/api/v1/banners").GetProperty("get")
            .GetProperty("security").GetArrayLength().Should().Be(4);
        paths.GetProperty("/api/v1/admin/banners").GetProperty("get")
            .GetProperty("security")[0].TryGetProperty("CustomerBearer", out _)
            .Should().BeTrue();
        paths.GetProperty("/api/v1/admin/banners/{id}").GetProperty("delete")
            .GetProperty("parameters").EnumerateArray()
            .Should().Contain(parameter =>
                parameter.GetProperty("name").GetString() == "expectedRowVersion");
        paths.GetProperty("/api/v1/banners").GetProperty("get")
            .GetProperty("responses").TryGetProperty("429", out _).Should().BeTrue();
        paths.GetProperty("/api/v1/admin/banners").GetProperty("get")
            .GetProperty("responses").TryGetProperty("429", out _).Should().BeTrue();
        var itemPath = paths.GetProperty("/api/v1/admin/banners/{id}");
        itemPath.GetProperty("put").GetProperty("responses")
            .TryGetProperty("404", out _).Should().BeTrue();
        itemPath.GetProperty("put").GetProperty("responses")
            .TryGetProperty("409", out _).Should().BeTrue();
        itemPath.GetProperty("delete").GetProperty("responses")
            .TryGetProperty("409", out _).Should().BeTrue();
    }

    private static Banner BannerAt(
        string id,
        int order,
        bool active,
        string suffix = "production") => new()
    {
        Id = Guid.Parse(id),
        ImageUrl = $"https://cdn.example.test/banners/{suffix}.png",
        DisplayOrder = order,
        IsActive = active,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        RowVersion = new byte[8]
    };

    private static async Task SeedAsync(CatalogApiFactory factory, params Banner[] banners)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Banners.AddRange(banners);
        await context.SaveChangesAsync();
    }

    private static async Task SeedDemoAsync(
        CatalogApiFactory factory,
        CustomerDevice device,
        Banner banner)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICustomerDataPartitionContext>()
            .SetTrustedPartition(DataPartitionNames.Demo);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.CustomerDevices.Add(device);
        context.Banners.Add(banner);
        await context.SaveChangesAsync();
    }

    private static string Jwt(
        string role,
        string secret = CatalogApiIntegrationTests.JwtSecret,
        string partition = DataPartitionNames.Production)
    {
        var token = new JwtSecurityToken(
            issuer: CatalogApiIntegrationTests.JwtIssuer,
            audience: CatalogApiIntegrationTests.JwtAudience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim(DataPartitionNames.ClaimType, partition)
            ],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("code").GetString();
    }

    private sealed class ThrowingBannerService : IBannerService
    {
        private const string Message = "banner-database-secret";

        public Task<PublicBannersResponse> GetPublicAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);

        public Task<AdminBannersResponse> GetAdminAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);

        public Task<AdminBannerResponse> CreateAsync(
            CreateBannerRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);

        public Task<AdminBannerResponse> UpdateAsync(
            Guid id,
            UpdateBannerRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);

        public Task DeleteAsync(
            Guid id,
            string expectedRowVersion,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Message);
    }
}
