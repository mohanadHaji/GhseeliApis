using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Exercises favourite idempotency races through TestServer and SQL Server.
/// </summary>
public sealed class BusinessFavouriteRelationalHttpTests
{
    [Fact]
    public async Task Concurrent_puts_both_return_204_and_create_one_row()
    {
        await using var factory = new RelationalFavouriteApiFactory();
        var seeded = await factory.SeedAsync();
        using var first = factory.CreateApiClient(seeded.UserId);
        using var second = factory.CreateApiClient(seeded.UserId);
        factory.SynchronizeNextFavouriteReads();

        var responses = await Task.WhenAll(
            first.PutAsync(FavouriteUrl(seeded.BusinessId), null),
            second.PutAsync(FavouriteUrl(seeded.BusinessId), null));
        try
        {
            responses.Should().OnlyContain(response =>
                response.StatusCode == HttpStatusCode.NoContent);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var verify = factory.CreateContext();
        (await verify.BusinessFavourites.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_deletes_both_return_204_and_leave_zero_rows()
    {
        await using var factory = new RelationalFavouriteApiFactory();
        var seeded = await factory.SeedAsync();
        await using (var setup = factory.CreateContext())
        {
            setup.BusinessFavourites.Add(new BusinessFavourite
            {
                UserId = seeded.UserId,
                BusinessSourceId = seeded.BusinessSourceId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await setup.SaveChangesAsync();
        }
        using var first = factory.CreateApiClient(seeded.UserId);
        using var second = factory.CreateApiClient(seeded.UserId);

        var responses = await Task.WhenAll(
            first.DeleteAsync(FavouriteUrl(seeded.BusinessId)),
            second.DeleteAsync(FavouriteUrl(seeded.BusinessId)));
        try
        {
            responses.Should().OnlyContain(response =>
                response.StatusCode == HttpStatusCode.NoContent);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var verify = factory.CreateContext();
        (await verify.BusinessFavourites.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private static string FavouriteUrl(Guid businessId) =>
        $"/api/v1/catalog/businesses/{businessId:D}/favourite";

    private sealed class RelationalFavouriteApiFactory :
        WebApplicationFactory<Program>,
        IAsyncDisposable
    {
        private const string Secret = "FavouriteHttpTestsSecret_Minimum32Characters";
        private const string Issuer = "GhseeliApis.FavouriteHttpTests";
        private const string Audience = "GhseeliApis.FavouriteHttpClients";
        private readonly SqlServerCatalogDatabase _database =
            SqlServerCatalogDatabase.CreateAsync().GetAwaiter().GetResult();
        private readonly FavouriteReadBarrierInterceptor _barrier = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:CustomerConnection", _database.ConnectionString);
            builder.UseSetting("JwtSettings:SecretKey", Secret);
            builder.UseSetting("JwtSettings:Issuer", Issuer);
            builder.UseSetting("JwtSettings:Audience", Audience);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
                services.RemoveAll<ApplicationDbContext>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlServer(_database.ConnectionString)
                        .AddInterceptors(_barrier));
            });
        }

        public async Task<(Guid UserId, Guid BusinessId, Guid BusinessSourceId)> SeedAsync()
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = $"{Guid.NewGuid():N}@example.test",
                NormalizedUserName = $"{Guid.NewGuid():N}@EXAMPLE.TEST",
                Email = $"{Guid.NewGuid():N}@example.test",
                NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.TEST",
                FullName = "Favourite Owner",
                IsActive = true
            };
            var provider = new CatalogProviderReadModel
            {
                Id = Guid.NewGuid(),
                SourceCompanyId = Guid.NewGuid(),
                IsEnabled = true,
                NameAr = "مزود",
                CatalogVersion = 1,
                SnapshotHash = "favourite-concurrency",
                LastSuccessfulRefreshAtUtc = DateTimeOffset.UtcNow
            };
            await _database.ExecuteAsync(context =>
            {
                context.Users.Add(user);
                context.CatalogProviders.Add(provider);
            });
            return (user.Id, provider.Id, provider.SourceCompanyId);
        }

        public void SynchronizeNextFavouriteReads() => _barrier.Arm();

        public HttpClient CreateApiClient(Guid userId)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateJwt(userId));
            return client;
        }

        public ApplicationDbContext CreateContext() => _database.CreateContext();

        public new async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _database.DisposeAsync();
        }

        private static string CreateJwt(Guid userId)
        {
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                SecurityAlgorithms.HmacSha256);
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                Issuer,
                Audience,
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, "User"),
                    new Claim(DataPartitionNames.ClaimType, DataPartitionNames.Production)
                ],
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: credentials));
        }
    }

    private sealed class FavouriteReadBarrierInterceptor : DbCommandInterceptor
    {
        private readonly object _gate = new();
        private TaskCompletionSource? _release;
        private int _arrivals;

        public void Arm()
        {
            lock (_gate)
            {
                _arrivals = 0;
                _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Task? wait = null;
            lock (_gate)
            {
                if (_release is not null &&
                    command.CommandText.Contains("[BusinessFavourites]", StringComparison.Ordinal) &&
                    command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    _arrivals++;
                    if (_arrivals == 2)
                    {
                        _release.TrySetResult();
                    }
                    wait = _release.Task;
                }
            }

            if (wait is not null)
            {
                await wait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return await base.ReaderExecutingAsync(
                command, eventData, result, cancellationToken);
        }
    }
}
