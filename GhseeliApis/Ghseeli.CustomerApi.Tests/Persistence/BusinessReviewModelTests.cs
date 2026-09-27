using FluentAssertions;
using GhseeliApis.DataPartitioning;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Persistence;

/// <summary>
/// Verifies BusinessReview schema and trusted partition behavior.
/// </summary>
public sealed class BusinessReviewModelTests
{
    [Fact]
    public void Model_has_review_constraints_relations_indexes_and_rowversion()
    {
        using var context = CreateContext(new CustomerDataPartitionContext());
        var entity = context.Model.FindEntityType(typeof(BusinessReview))!;

        entity.GetTableName().Should().Be("BusinessReviews");
        entity.FindProperty(nameof(BusinessReview.Comment))!.GetMaxLength().Should().Be(1000);
        entity.FindProperty(nameof(BusinessReview.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        entity.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { "CustomerBookingId" }));
        entity.GetForeignKeys().Select(value => value.PrincipalEntityType.ClrType)
            .Should().Contain([typeof(User), typeof(CustomerBooking)]);
    }

    [Fact]
    public async Task Save_assigns_trusted_partition_and_global_filter_isolates_rows()
    {
        var name = Guid.NewGuid().ToString("N");
        var demoPartition = new CustomerDataPartitionContext();
        demoPartition.SetTrustedPartition("Demo");
        await using (var demo = CreateContext(demoPartition, name))
        {
            demo.BusinessReviews.Add(new BusinessReview
            {
                CustomerBookingId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                BusinessSourceId = Guid.NewGuid(),
                Rating = 5,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await demo.SaveChangesAsync();
            (await demo.BusinessReviews.SingleAsync()).IsDemo.Should().BeTrue();
        }

        await using var production = CreateContext(new CustomerDataPartitionContext(), name);
        (await production.BusinessReviews.CountAsync()).Should().Be(0);
        (await production.BusinessReviews.IgnoreQueryFilters().SingleAsync())
            .IsDemo.Should().BeTrue();
    }

    private static ApplicationDbContext CreateContext(
        ICustomerDataPartitionContext partition,
        string? name = null) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
                .Options,
            partition);
}
