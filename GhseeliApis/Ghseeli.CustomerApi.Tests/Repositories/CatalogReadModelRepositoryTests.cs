using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Services.Catalog;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Repositories;

/// <summary>
/// Exercises catalog provider synchronization persistence behavior.
/// </summary>
public class CatalogReadModelRepositoryTests
{
    [Fact]
    public async Task SynchronizeConfiguredProvidersAsync_DisablesMissingProviderWithoutDeletingCachedGraph()
    {
        var existingSourceCompanyId = Guid.NewGuid();
        var newSourceCompanyId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.CatalogProviders.Add(CreateProvider(existingSourceCompanyId));
        await context.SaveChangesAsync();
        var repository = new CatalogReadModelRepository(context);

        await repository.SynchronizeConfiguredProvidersAsync(
            [
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = newSourceCompanyId,
                    Enabled = true,
                    Order = 3
                }
            ],
            default);

        var disabledProvider = await context.CatalogProviders
            .Include(provider => provider.Branches)
            .Include(provider => provider.Categories)
                .ThenInclude(category => category.Offerings)
            .SingleAsync(provider => provider.SourceCompanyId == existingSourceCompanyId);
        var newProvider = await context.CatalogProviders
            .SingleAsync(provider => provider.SourceCompanyId == newSourceCompanyId);

        disabledProvider.IsEnabled.Should().BeFalse();
        disabledProvider.Branches.Should().ContainSingle();
        disabledProvider.Categories.Should().ContainSingle();
        disabledProvider.Categories.Single().Offerings.Should().ContainSingle();
        newProvider.IsEnabled.Should().BeTrue();
        newProvider.DisplayOrder.Should().Be(3);
    }

    [Fact]
    public async Task SynchronizeConfiguredProvidersAsync_UpdatesExistingOrderingWithoutCreatingDuplicates()
    {
        var sourceCompanyId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.CatalogProviders.Add(new CatalogProviderReadModel
        {
            Id = Guid.NewGuid(),
            SourceCompanyId = sourceCompanyId,
            IsEnabled = false,
            DisplayOrder = 0
        });
        await context.SaveChangesAsync();
        var repository = new CatalogReadModelRepository(context);

        await repository.SynchronizeConfiguredProvidersAsync(
            [
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = sourceCompanyId,
                    Enabled = true,
                    Order = 9
                }
            ],
            default);

        context.CatalogProviders.Should().ContainSingle();
        var provider = await context.CatalogProviders.SingleAsync();
        provider.IsEnabled.Should().BeTrue();
        provider.DisplayOrder.Should().Be(9);
    }

    private static CatalogProviderReadModel CreateProvider(Guid sourceCompanyId)
    {
        var provider = new CatalogProviderReadModel
        {
            Id = Guid.NewGuid(),
            SourceCompanyId = sourceCompanyId,
            IsEnabled = true,
            DisplayOrder = 1,
            NameAr = "مغسلة",
            CatalogVersion = 3,
            SnapshotHash = "hash",
            LastSuccessfulRefreshAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var branch = new CatalogBranchReadModel
        {
            Id = Guid.NewGuid(),
            SourceBranchId = Guid.NewGuid(),
            Provider = provider,
            ProviderId = provider.Id,
            NameAr = "الفرع",
            AddressAr = "العنوان",
            HasPublishedServiceArea = true,
            ServiceAreaRadiusKm = 10
        };
        var category = new CatalogCategoryReadModel
        {
            Id = Guid.NewGuid(),
            SourceCategoryId = Guid.NewGuid(),
            Provider = provider,
            ProviderId = provider.Id,
            NameAr = "الفئة",
            DisplayOrder = 1
        };
        var offering = new CatalogOfferingReadModel
        {
            Id = Guid.NewGuid(),
            SourceOfferingId = Guid.NewGuid(),
            Category = category,
            CategoryId = category.Id,
            Branch = branch,
            BranchId = branch.Id,
            NameAr = "الخدمة",
            BasePrice = 25,
            DurationMinutes = 20,
            DisplayOrder = 1
        };

        provider.Branches.Add(branch);
        provider.Categories.Add(category);
        category.Offerings.Add(offering);

        return provider;
    }
}
