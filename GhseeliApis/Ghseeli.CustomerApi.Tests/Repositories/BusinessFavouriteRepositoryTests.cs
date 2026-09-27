using FluentAssertions;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.DataPartitioning;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Repositories;

/// <summary>
/// Defines favourite persistence idempotency and partition isolation.
/// </summary>
public sealed class BusinessFavouriteRepositoryTests
{
    [Fact]
    public async Task AddAndDelete_RepeatedCalls_AreIdempotentAndPartitioned()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var userId = Guid.NewGuid();
        var demoUserId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        await SeedUserAsync(options, userId, DataPartitionNames.Production);
        await SeedUserAsync(options, demoUserId, DataPartitionNames.Demo);

        await using (var production = CreateContext(options, DataPartitionNames.Production))
        {
            var repository = new BusinessFavouriteRepository(production);
            await repository.AddIfMissingAsync(
                userId, sourceId, DateTimeOffset.UtcNow, CancellationToken.None);
            await repository.AddIfMissingAsync(
                userId, sourceId, DateTimeOffset.UtcNow, CancellationToken.None);
            (await production.BusinessFavourites.CountAsync()).Should().Be(1);
        }

        await using (var demo = CreateContext(options, DataPartitionNames.Demo))
        {
            var repository = new BusinessFavouriteRepository(demo);
            (await demo.BusinessFavourites.CountAsync()).Should().Be(0);
            await repository.AddIfMissingAsync(
                demoUserId, sourceId, DateTimeOffset.UtcNow, CancellationToken.None);
            (await demo.BusinessFavourites.CountAsync()).Should().Be(1);
            await repository.DeleteIfPresentAsync(demoUserId, sourceId, CancellationToken.None);
            await repository.DeleteIfPresentAsync(demoUserId, sourceId, CancellationToken.None);
            (await demo.BusinessFavourites.CountAsync()).Should().Be(0);
        }

        await using var verifyProduction =
            CreateContext(options, DataPartitionNames.Production);
        (await verifyProduction.BusinessFavourites.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Relational_DemoAndProductionIdentityDuplicateAndReconciliation_ArePartitionSafe()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var sourceId = Guid.NewGuid();
        var productionUserId = Guid.NewGuid();
        var demoUserId = Guid.NewGuid();
        await using (var production = database.CreateContext(DataPartitionNames.Production))
        {
            production.Users.Add(CreateUser(productionUserId, "production"));
            await production.SaveChangesAsync();
        }
        await using (var demo = database.CreateContext(DataPartitionNames.Demo))
        {
            demo.Users.Add(CreateUser(demoUserId, "demo"));
            await demo.SaveChangesAsync();
        }

        await using (var production = database.CreateContext(DataPartitionNames.Production))
        {
            var repository = new BusinessFavouriteRepository(production);
            await repository.AddIfMissingAsync(
                productionUserId, sourceId, DateTimeOffset.UtcNow, CancellationToken.None);
            await repository.AddIfMissingAsync(
                productionUserId, sourceId, DateTimeOffset.UtcNow, CancellationToken.None);
            (await repository.GetBusinessSourceIdsAsync(
                productionUserId, [sourceId], CancellationToken.None))
                .Should().Equal(sourceId);
            (await production.BusinessFavourites.CountAsync()).Should().Be(1);
        }

        await using (var demo = database.CreateContext(DataPartitionNames.Demo))
        {
            var repository = new BusinessFavouriteRepository(demo);
            (await repository.GetBusinessSourceIdsAsync(
                demoUserId, [sourceId], CancellationToken.None)).Should().BeEmpty();
            await repository.AddIfMissingAsync(
                demoUserId, sourceId, DateTimeOffset.UtcNow, CancellationToken.None);
            await repository.AddIfMissingAsync(
                demoUserId, sourceId, DateTimeOffset.UtcNow, CancellationToken.None);
            (await demo.BusinessFavourites.CountAsync()).Should().Be(1);
            await repository.DeleteIfPresentAsync(
                demoUserId, sourceId, CancellationToken.None);
            (await repository.GetBusinessSourceIdsAsync(
                demoUserId, [sourceId], CancellationToken.None)).Should().BeEmpty();
            await repository.AddIfMissingAsync(
                demoUserId, sourceId, DateTimeOffset.UtcNow, CancellationToken.None);
            (await repository.GetBusinessSourceIdsAsync(
                demoUserId, [sourceId], CancellationToken.None))
                .Should().Equal(sourceId);
        }

        await using var verify = database.CreateContext(DataPartitionNames.Production);
        var all = await verify.BusinessFavourites.IgnoreQueryFilters().ToArrayAsync();
        all.Should().HaveCount(2);
        all.Count(value => value.IsDemo).Should().Be(1);
        all.Count(value => !value.IsDemo).Should().Be(1);
        all.Select(value => value.UserId)
            .Should().BeEquivalentTo([productionUserId, demoUserId]);
    }

    private static async Task SeedUserAsync(
        DbContextOptions<ApplicationDbContext> options,
        Guid userId,
        string partition)
    {
        await using var context = CreateContext(options, partition);
        context.Users.Add(new User
        {
            Id = userId,
            UserName = $"{partition}@example.test",
            Email = $"{partition}@example.test",
            FullName = partition
        });
        await context.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateContext(
        DbContextOptions<ApplicationDbContext> options,
        string partition)
    {
        var dataPartition = new CustomerDataPartitionContext();
        dataPartition.SetTrustedPartition(partition);
        return new ApplicationDbContext(options, dataPartition);
    }

    private static User CreateUser(Guid id, string name) => new()
    {
        Id = id,
        UserName = $"{name}-{id:N}@example.test",
        NormalizedUserName = $"{name}-{id:N}@EXAMPLE.TEST".ToUpperInvariant(),
        Email = $"{name}-{id:N}@example.test",
        NormalizedEmail = $"{name}-{id:N}@EXAMPLE.TEST".ToUpperInvariant(),
        FullName = name,
        IsActive = true
    };
}
