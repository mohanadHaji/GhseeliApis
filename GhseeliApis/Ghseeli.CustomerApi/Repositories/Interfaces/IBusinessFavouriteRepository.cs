namespace GhseeliApis.Repositories.Interfaces;

public interface IBusinessFavouriteRepository
{
    Task<IReadOnlySet<Guid>> GetBusinessSourceIdsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> businessSourceIds,
        CancellationToken cancellationToken);

    Task AddIfMissingAsync(
        Guid userId,
        Guid businessSourceId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken);

    Task DeleteIfPresentAsync(
        Guid userId,
        Guid businessSourceId,
        CancellationToken cancellationToken);
}
