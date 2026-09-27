using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Persistence;

/// <summary>
/// Defines the customer-owned favourite relational model.
/// </summary>
public sealed class BusinessFavouriteModelTests
{
    [Fact]
    public void Model_UsesPartitionedUserBusinessUniqueIndex()
    {
        using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var entity = context.Model.FindEntityType(typeof(BusinessFavourite))!;
        var index = entity.GetIndexes().Single(value =>
            value.Properties.Select(property => property.Name).SequenceEqual(
            [
                nameof(BusinessFavourite.UserId),
                nameof(BusinessFavourite.BusinessSourceId),
                nameof(BusinessFavourite.IsDemo)
            ]));

        index.IsUnique.Should().BeTrue();
        entity.GetQueryFilter().Should().NotBeNull();
    }
}
