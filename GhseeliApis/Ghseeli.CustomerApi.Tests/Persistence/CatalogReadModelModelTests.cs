using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Persistence;

/// <summary>
/// Verifies the persisted schema for customer catalog read models.
/// </summary>
public class CatalogReadModelModelTests
{
    [Fact]
    public void Model_UsesUniqueBusinessSourceIds_AndProviderConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();

        var providerEntity = context.Model.FindEntityType(typeof(CatalogProviderReadModel));
        var branchEntity = context.Model.FindEntityType(typeof(CatalogBranchReadModel));
        var categoryEntity = context.Model.FindEntityType(typeof(CatalogCategoryReadModel));
        var offeringEntity = context.Model.FindEntityType(typeof(CatalogOfferingReadModel));

        providerEntity.Should().NotBeNull();
        providerEntity!.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Single().Name == nameof(CatalogProviderReadModel.SourceCompanyId));
        providerEntity.FindProperty(nameof(CatalogProviderReadModel.RowVersion))!
            .IsConcurrencyToken.Should().BeTrue();

        branchEntity!.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Single().Name == nameof(CatalogBranchReadModel.SourceBranchId));
        categoryEntity!.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Single().Name == nameof(CatalogCategoryReadModel.SourceCategoryId));
        offeringEntity!.GetIndexes().Should().Contain(index =>
            index.IsUnique &&
            index.Properties.Single().Name == nameof(CatalogOfferingReadModel.SourceOfferingId));

        context.CatalogProviders.Should().BeEmpty();
        context.CatalogBranches.Should().BeEmpty();
        context.CatalogCategories.Should().BeEmpty();
        context.CatalogOfferings.Should().BeEmpty();
    }
}
