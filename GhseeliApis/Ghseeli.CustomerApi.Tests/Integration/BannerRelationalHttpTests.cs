using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Verifies banner Admin CRUD and optimistic-concurrency races through HTTP and SQL Server.
/// </summary>
public sealed class BannerRelationalHttpTests
{
    [Fact]
    public async Task Admin_crud_rotates_rowversion_rejects_stale_delete_then_accepts_current_delete()
    {
        await using var factory = new RelationalBannerApiFactory();
        using var client = factory.CreateAdminClient();

        var created = await CreateAsync(client, "created", 1, true);
        using var update = await client.PutAsJsonAsync(
            $"/api/v1/admin/banners/{created.Id:D}",
            Body("updated", 2, false, created.RowVersion));
        using var updateJson = await JsonDocument.ParseAsync(
            await update.Content.ReadAsStreamAsync());

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var currentVersion = updateJson.RootElement.GetProperty("rowVersion").GetString()!;
        currentVersion.Should().NotBe(created.RowVersion);
        Convert.FromBase64String(currentVersion).Should().HaveCount(8);

        using var staleDelete = await client.DeleteAsync(DeleteUrl(created.Id, created.RowVersion));
        staleDelete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(staleDelete)).Should().Be("banner_version_conflict");

        await using (var verify = factory.CreateContext())
        {
            var persisted = await verify.Banners.SingleAsync(value => value.Id == created.Id);
            persisted.ImageUrl.Should().EndWith("/updated.png");
            persisted.DisplayOrder.Should().Be(2);
            persisted.IsActive.Should().BeFalse();
            Convert.ToBase64String(persisted.RowVersion).Should().Be(currentVersion);
        }

        using var delete = await client.DeleteAsync(DeleteUrl(created.Id, currentVersion));
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var deleted = factory.CreateContext();
        (await deleted.Banners.IgnoreQueryFilters().CountAsync(value => value.Id == created.Id))
            .Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_updates_have_one_winner_one_stable_conflict_no_500_and_winner_state()
    {
        await using var factory = new RelationalBannerApiFactory();
        using var first = factory.CreateAdminClient();
        using var second = factory.CreateAdminClient();
        var created = await CreateAsync(first, "original", 1, true);

        factory.SynchronizeNextBannerReads();
        var responses = await Task.WhenAll(
            first.PutAsJsonAsync(
                $"/api/v1/admin/banners/{created.Id:D}",
                Body("first", 10, true, created.RowVersion)),
            second.PutAsJsonAsync(
                $"/api/v1/admin/banners/{created.Id:D}",
                Body("second", 20, false, created.RowVersion)));
        try
        {
            await AssertRaceAsync(responses, HttpStatusCode.OK);
            using var winnerJson = await JsonDocument.ParseAsync(
                await responses.Single(value => value.StatusCode == HttpStatusCode.OK)
                    .Content.ReadAsStreamAsync());
            var winnerVersion = winnerJson.RootElement.GetProperty("rowVersion").GetString()!;

            await using var verify = factory.CreateContext();
            var persisted = await verify.Banners.SingleAsync(value => value.Id == created.Id);
            (persisted.DisplayOrder, persisted.IsActive).Should().BeOneOf(
                (10, true),
                (20, false));
            Convert.ToBase64String(persisted.RowVersion).Should().Be(winnerVersion);
        }
        finally
        {
            DisposeAll(responses);
        }
    }

    [Fact]
    public async Task Concurrent_deletes_have_one_winner_one_stable_conflict_no_500_and_no_row()
    {
        await using var factory = new RelationalBannerApiFactory();
        using var first = factory.CreateAdminClient();
        using var second = factory.CreateAdminClient();
        var created = await CreateAsync(first, "delete", 1, true);

        factory.SynchronizeNextBannerReads();
        var responses = await Task.WhenAll(
            first.DeleteAsync(DeleteUrl(created.Id, created.RowVersion)),
            second.DeleteAsync(DeleteUrl(created.Id, created.RowVersion)));
        try
        {
            await AssertRaceAsync(responses, HttpStatusCode.NoContent);
        }
        finally
        {
            DisposeAll(responses);
        }

        await using var verify = factory.CreateContext();
        (await verify.Banners.IgnoreQueryFilters().CountAsync(value => value.Id == created.Id))
            .Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_update_delete_have_one_winner_stable_conflict_no_500_and_consistent_state()
    {
        await using var factory = new RelationalBannerApiFactory();
        using var updateClient = factory.CreateAdminClient();
        using var deleteClient = factory.CreateAdminClient();
        var created = await CreateAsync(updateClient, "race", 1, true);

        factory.SynchronizeNextBannerReads();
        var updateTask = updateClient.PutAsJsonAsync(
            $"/api/v1/admin/banners/{created.Id:D}",
            Body("winner", 7, false, created.RowVersion));
        var deleteTask = deleteClient.DeleteAsync(DeleteUrl(created.Id, created.RowVersion));
        await Task.WhenAll(updateTask, deleteTask);
        using var update = await updateTask;
        using var delete = await deleteTask;
        var responses = new[] { update, delete };

        responses.Should().NotContain(value =>
            value.StatusCode == HttpStatusCode.InternalServerError ||
            value.StatusCode == HttpStatusCode.NotFound);
        responses.Count(value => value.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        (await ProblemCodeAsync(responses.Single(value =>
            value.StatusCode == HttpStatusCode.Conflict))).Should().Be("banner_version_conflict");

        await using var verify = factory.CreateContext();
        var persisted = await verify.Banners.IgnoreQueryFilters()
            .SingleOrDefaultAsync(value => value.Id == created.Id);
        if (update.StatusCode == HttpStatusCode.OK)
        {
            delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
            persisted.Should().NotBeNull();
            persisted!.DisplayOrder.Should().Be(7);
            persisted.IsActive.Should().BeFalse();
        }
        else
        {
            update.StatusCode.Should().Be(HttpStatusCode.Conflict);
            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
            persisted.Should().BeNull();
        }
    }

    private static object Body(
        string suffix,
        int displayOrder,
        bool isActive,
        string expectedRowVersion) => new
        {
            imageUrl = $"https://cdn.example.test/banners/{suffix}.png",
            displayOrder,
            isActive,
            expectedRowVersion
        };

    private static async Task<(Guid Id, string RowVersion)> CreateAsync(
        HttpClient client,
        string suffix,
        int displayOrder,
        bool isActive)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/banners",
            new
            {
                imageUrl = $"https://cdn.example.test/banners/{suffix}.png",
                displayOrder,
                isActive
            });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return (
            json.RootElement.GetProperty("id").GetGuid(),
            json.RootElement.GetProperty("rowVersion").GetString()!);
    }

    private static string DeleteUrl(Guid id, string rowVersion) =>
        $"/api/v1/admin/banners/{id:D}?expectedRowVersion={Uri.EscapeDataString(rowVersion)}";

    private static async Task AssertRaceAsync(
        IReadOnlyCollection<HttpResponseMessage> responses,
        HttpStatusCode winnerStatus)
    {
        responses.Select(value => value.StatusCode).Order().Should().Equal(
            new[] { winnerStatus, HttpStatusCode.Conflict }.Order());
        responses.Should().NotContain(value =>
            value.StatusCode == HttpStatusCode.InternalServerError ||
            value.StatusCode == HttpStatusCode.NotFound);
        (await ProblemCodeAsync(responses.Single(value =>
            value.StatusCode == HttpStatusCode.Conflict))).Should()
            .Be("banner_version_conflict");
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("code").GetString();
    }

    private static void DisposeAll(IEnumerable<HttpResponseMessage> responses)
    {
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private sealed class RelationalBannerApiFactory :
        WebApplicationFactory<Program>,
        IAsyncDisposable
    {
        private const string JwtSecret = "BannerRelationalHttpTestsSecret_Minimum32Characters";
        private const string JwtIssuer = "GhseeliApis.BannerRelationalTests";
        private const string JwtAudience = "GhseeliApis.BannerRelationalClients";
        private readonly SqlServerCatalogDatabase _database =
            SqlServerCatalogDatabase.CreateAsync().GetAwaiter().GetResult();
        private readonly BannerReadBarrierInterceptor _barrier = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:CustomerConnection", _database.ConnectionString);
            builder.UseSetting("JwtSettings:SecretKey", JwtSecret);
            builder.UseSetting("JwtSettings:Issuer", JwtIssuer);
            builder.UseSetting("JwtSettings:Audience", JwtAudience);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<IBannerRepository>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlServer(_database.ConnectionString));
                services.AddScoped<IBannerRepository>(provider =>
                    new BarrierBannerRepository(
                        new BannerRepository(
                            provider.GetRequiredService<ApplicationDbContext>()),
                        _barrier));
            });
        }

        public void SynchronizeNextBannerReads() => _barrier.Arm();

        public HttpClient CreateAdminClient()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateJwt());
            return client;
        }

        public ApplicationDbContext CreateContext() => _database.CreateContext();

        public new async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _database.DisposeAsync();
        }

        private static string CreateJwt()
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: JwtIssuer,
                audience: JwtAudience,
                claims:
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.Role, "Admin"),
                    new Claim(DataPartitionNames.ClaimType, DataPartitionNames.Production)
                ],
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: new SigningCredentials(
                    key, SecurityAlgorithms.HmacSha256)));
        }
    }

    private sealed class BarrierBannerRepository : IBannerRepository
    {
        private readonly IBannerRepository _inner;
        private readonly BannerReadBarrierInterceptor _barrier;

        public BarrierBannerRepository(
            IBannerRepository inner,
            BannerReadBarrierInterceptor barrier)
        {
            _inner = inner;
            _barrier = barrier;
        }

        public Task<IReadOnlyList<Banner>> GetPublicAsync(CancellationToken cancellationToken) =>
            _inner.GetPublicAsync(cancellationToken);

        public Task<IReadOnlyList<Banner>> GetAdminAsync(CancellationToken cancellationToken) =>
            _inner.GetAdminAsync(cancellationToken);

        public async Task<Banner?> GetTrackedAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            var banner = await _inner.GetTrackedAsync(id, cancellationToken);
            await _barrier.WaitAsync(cancellationToken);
            return banner;
        }

        public Task AddAsync(Banner banner, CancellationToken cancellationToken) =>
            _inner.AddAsync(banner, cancellationToken);

        public void Delete(Banner banner) => _inner.Delete(banner);

        public Task SaveAsync(CancellationToken cancellationToken) =>
            _inner.SaveAsync(cancellationToken);
    }

    private sealed class BannerReadBarrierInterceptor
    {
        private TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _remaining;

        public void Arm()
        {
            _release = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _remaining, 2);
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _remaining) > 0)
            {
                if (Interlocked.Decrement(ref _remaining) == 0)
                {
                    _release.TrySetResult();
                }

                await _release.Task.WaitAsync(cancellationToken);
            }
        }
    }
}
