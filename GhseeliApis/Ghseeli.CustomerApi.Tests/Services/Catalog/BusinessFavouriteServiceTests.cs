using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Catalog;
using Moq;

namespace GhseeliApis.Tests.Services.Catalog;

/// <summary>
/// Defines favourite target validation and mutation behavior.
/// </summary>
public sealed class BusinessFavouriteServiceTests
{
    [Fact]
    public async Task AddAsync_ActiveBusiness_UsesTrustedSourceId()
    {
        var businessId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var catalog = new Mock<ICatalogReadModelRepository>();
        catalog.Setup(value => value.GetEnabledProviderSummaryAsync(
                businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProviderReadModel
            {
                Id = businessId,
                SourceCompanyId = sourceId,
                IsEnabled = true
            });
        var favourites = new Mock<IBusinessFavouriteRepository>();
        var service = new BusinessFavouriteService(
            catalog.Object, favourites.Object, TimeProvider.System);

        await service.AddAsync(businessId, userId, CancellationToken.None);

        favourites.Verify(value => value.AddIfMissingAsync(
            userId,
            sourceId,
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_UnknownOrPartitionHiddenBusiness_IsNonDisclosing()
    {
        var catalog = new Mock<ICatalogReadModelRepository>();
        catalog.Setup(value => value.GetEnabledProviderSummaryAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogProviderReadModel?)null);
        var service = new BusinessFavouriteService(
            catalog.Object,
            Mock.Of<IBusinessFavouriteRepository>(),
            TimeProvider.System);

        var action = () => service.DeleteAsync(
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        await action.Should().ThrowAsync<CatalogReadModelException>()
            .Where(exception =>
                exception.StatusCode == 404 &&
                exception.Code == CatalogProblemCodes.BusinessNotFound);
    }
}
