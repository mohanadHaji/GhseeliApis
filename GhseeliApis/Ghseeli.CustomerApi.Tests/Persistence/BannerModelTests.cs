using FluentAssertions;
using GhseeliApis.DataPartitioning;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Persistence;

/// <summary>
/// Verifies banner storage constraints and trusted partition behavior.
/// </summary>
public sealed class BannerModelTests
{
    [Fact]
    public async Task Added_banner_receives_trusted_demo_partition_and_query_filter_isolates_it()
    {
        var database = $"banner-model-{Guid.NewGuid():N}";
        var demoPartition = new CustomerDataPartitionContext();
        demoPartition.SetTrustedPartition("Demo");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(database)
            .Options;
        var id = Guid.NewGuid();

        await using (var demo = new ApplicationDbContext(options, demoPartition))
        {
            demo.Banners.Add(new Banner
            {
                Id = id,
                ImageUrl = "https://cdn.example.test/demo.png",
                DisplayOrder = 1,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await demo.SaveChangesAsync();
            (await demo.Banners.SingleAsync()).IsDemo.Should().BeTrue();
        }

        await using var production = new ApplicationDbContext(
            options, new CustomerDataPartitionContext());
        (await production.Banners.CountAsync()).Should().Be(0);
        (await production.Banners.IgnoreQueryFilters().SingleAsync()).Id.Should().Be(id);
    }

    [Fact]
    public void Relational_model_has_required_url_rowversion_checks_and_ordering_index()
    {
        using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=BannerModel;Trusted_Connection=True;")
                .Options);
        var entity = context.Model.FindEntityType(typeof(Banner))!;

        entity.FindProperty(nameof(Banner.ImageUrl))!.IsNullable.Should().BeFalse();
        entity.FindProperty(nameof(Banner.ImageUrl))!.GetMaxLength().Should().Be(500);
        entity.FindProperty(nameof(Banner.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        entity.GetIndexes().Should().Contain(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[]
                {
                    nameof(Banner.IsDemo),
                    nameof(Banner.IsActive),
                    nameof(Banner.DisplayOrder),
                    nameof(Banner.Id)
                }));
    }
}
