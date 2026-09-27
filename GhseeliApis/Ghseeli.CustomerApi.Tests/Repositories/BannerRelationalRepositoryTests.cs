using FluentAssertions;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.Models;
using GhseeliApis.Repositories;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Repositories;

/// <summary>
/// Verifies banner ordering, partition isolation, and SQL rowversion concurrency.
/// </summary>
public sealed class BannerRelationalRepositoryTests
{
    [Fact]
    public async Task Public_and_admin_queries_apply_state_order_and_partition_filters()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var productionIds = new[]
        {
            Guid.Parse("10000000-0000-0000-0000-000000000003"),
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("10000000-0000-0000-0000-000000000002")
        };
        await using (var production = database.CreateContext())
        {
            production.Banners.AddRange(
                Create(productionIds[0], 10, true),
                Create(productionIds[1], 10, true),
                Create(productionIds[2], 1, false));
            await production.SaveChangesAsync();
        }
        await using (var demo = database.CreateContext(DataPartitionNames.Demo))
        {
            demo.Banners.Add(Create(Guid.NewGuid(), 0, true));
            await demo.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var repository = new BannerRepository(context);
        var publicRows = await repository.GetPublicAsync(default);
        var adminRows = await repository.GetAdminAsync(default);

        publicRows.Select(value => value.Id).Should().Equal(
            productionIds[1], productionIds[0]);
        adminRows.Select(value => value.Id).Should().Equal(
            productionIds[2], productionIds[1], productionIds[0]);
        adminRows.Should().OnlyContain(value => !value.IsDemo);
    }

    [Fact]
    public async Task Same_rowversion_allows_one_update_and_rejects_the_stale_writer()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            seed.Banners.Add(Create(id, 1, true));
            await seed.SaveChangesAsync();
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstBanner = await first.Banners.SingleAsync(value => value.Id == id);
        var secondBanner = await second.Banners.SingleAsync(value => value.Id == id);
        var originalVersion = firstBanner.RowVersion.ToArray();
        secondBanner.RowVersion.Should().Equal(originalVersion);

        firstBanner.DisplayOrder = 2;
        await first.SaveChangesAsync();
        firstBanner.RowVersion.Should().NotEqual(originalVersion);

        secondBanner.DisplayOrder = 3;
        var action = () => second.SaveChangesAsync();
        await action.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verify = database.CreateContext();
        var persisted = await verify.Banners.SingleAsync(value => value.Id == id);
        persisted.DisplayOrder.Should().Be(2);
        persisted.RowVersion.Should().Equal(firstBanner.RowVersion);
    }

    private static Banner Create(Guid id, int order, bool active) => new()
    {
        Id = id,
        ImageUrl = $"https://cdn.example.test/banners/{id:N}.png",
        DisplayOrder = order,
        IsActive = active,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };
}
