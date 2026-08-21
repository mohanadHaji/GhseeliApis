using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

public interface ICatalogReadModelRepository
{
    Task SynchronizeConfiguredProvidersAsync(
        IReadOnlyCollection<Services.Catalog.CatalogProviderRegistrationOptions> providers,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogProviderReadModel>> ListEnabledProviderSummariesAsync(
        CancellationToken cancellationToken);

    Task<CatalogProviderReadModel?> GetEnabledProviderSummaryAsync(
        Guid providerId,
        CancellationToken cancellationToken);

    Task<CatalogBranchReadModel?> GetEnabledBranchSummaryAsync(
        Guid branchId,
        CancellationToken cancellationToken);

    Task<CatalogCategoryReadModel?> GetEnabledCategorySummaryAsync(
        Guid categoryId,
        CancellationToken cancellationToken);

    Task<CatalogOfferingReadModel?> GetEnabledOfferingSummaryAsync(
        Guid offeringId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogProviderReadModel>> GetEnabledProvidersWithGraphAsync(
        IReadOnlyCollection<Guid>? providerIds,
        CancellationToken cancellationToken);

    Task<bool> TryAcquireRefreshLeaseAsync(
        Guid providerId,
        string leaseToken,
        DateTimeOffset acquiredAtUtc,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken);

    Task ReleaseRefreshLeaseAsync(
        Guid providerId,
        string leaseToken,
        DateTimeOffset failedAtUtc,
        string failureCode,
        CancellationToken cancellationToken);

    Task<CatalogSnapshotApplyResult> ApplySnapshotAsync(
        Guid providerId,
        CatalogSnapshotResponse snapshot,
        string snapshotHash,
        DateTimeOffset refreshedAtUtc,
        string leaseToken,
        CancellationToken cancellationToken);
}

public enum CatalogSnapshotApplyResult
{
    Applied = 0,
    VerifiedUnchanged = 1,
    RejectedVersionRegression = 2,
    LeaseLost = 3
}
