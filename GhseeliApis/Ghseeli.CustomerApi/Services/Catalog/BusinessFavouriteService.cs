using GhseeliApis.Repositories.Interfaces;

namespace GhseeliApis.Services.Catalog;

public interface IBusinessFavouriteService
{
    Task AddAsync(Guid businessId, Guid userId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid businessId, Guid userId, CancellationToken cancellationToken);
}

public sealed class BusinessFavouriteService : IBusinessFavouriteService
{
    private readonly ICatalogReadModelRepository _catalogRepository;
    private readonly IBusinessFavouriteRepository _favouriteRepository;
    private readonly TimeProvider _timeProvider;

    public BusinessFavouriteService(
        ICatalogReadModelRepository catalogRepository,
        IBusinessFavouriteRepository favouriteRepository,
        TimeProvider timeProvider)
    {
        _catalogRepository = catalogRepository;
        _favouriteRepository = favouriteRepository;
        _timeProvider = timeProvider;
    }

    public async Task AddAsync(
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var provider = await ResolveActiveBusinessAsync(businessId, cancellationToken);
        await _favouriteRepository.AddIfMissingAsync(
            userId,
            provider.SourceCompanyId,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public async Task DeleteAsync(
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var provider = await ResolveActiveBusinessAsync(businessId, cancellationToken);
        await _favouriteRepository.DeleteIfPresentAsync(
            userId,
            provider.SourceCompanyId,
            cancellationToken);
    }

    private async Task<Models.CatalogProviderReadModel> ResolveActiveBusinessAsync(
        Guid businessId,
        CancellationToken cancellationToken) =>
        await _catalogRepository.GetEnabledProviderSummaryAsync(businessId, cancellationToken)
        ?? throw new CatalogReadModelException(
            CatalogProblemCodes.BusinessNotFound,
            StatusCodes.Status404NotFound,
            "The requested business was not found.");
}
