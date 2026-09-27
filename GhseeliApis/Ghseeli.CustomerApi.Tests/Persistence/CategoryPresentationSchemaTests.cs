using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Persistence;

/// <summary>
/// Verifies the customer category read-model presentation column contract.
/// </summary>
public sealed class CategoryPresentationSchemaTests
{
    [Fact]
    public void CatalogCategory_PresentationColumns_AreNullableAndBounded()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=CategoryPresentationSchema;Trusted_Connection=True")
            .Options;
        using var context = new ApplicationDbContext(options);
        var entity = context.Model.FindEntityType(typeof(CatalogCategoryReadModel))!;

        var imageUrl = entity.FindProperty(nameof(CatalogCategoryReadModel.ImageUrl))!;
        imageUrl.IsNullable.Should().BeTrue();
        imageUrl.GetMaxLength().Should().Be(500);

        var colorHex = entity.FindProperty(nameof(CatalogCategoryReadModel.ColorHex))!;
        colorHex.IsNullable.Should().BeTrue();
        colorHex.GetMaxLength().Should().Be(7);

        var provider = context.Model.FindEntityType(typeof(CatalogProviderReadModel))!;
        provider.GetIndexes().Should().ContainSingle(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                new[]
                {
                    nameof(CatalogProviderReadModel.SourceCompanyId),
                    nameof(CatalogProviderReadModel.IsDemo)
                }));
        entity.GetIndexes().Should().ContainSingle(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                new[]
                {
                    nameof(CatalogCategoryReadModel.ProviderId),
                    nameof(CatalogCategoryReadModel.SourceCategoryId)
                }));
    }
}
