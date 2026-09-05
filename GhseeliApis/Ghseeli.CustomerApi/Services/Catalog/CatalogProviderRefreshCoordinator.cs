using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Business;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Services.Catalog;

public interface ICatalogProviderRefreshCoordinator
{
    Task EnsureProviderUsableAsync(
        CatalogProviderReadModel provider,
        bool forceRefresh,
        CancellationToken cancellationToken);
}

public sealed class CatalogProviderRefreshCoordinator : ICatalogProviderRefreshCoordinator
{
    private readonly ICatalogReadModelRepository _repository;
    private readonly IBusinessApiClient _businessApiClient;
    private readonly IOptionsMonitor<CatalogReadModelOptions> _optionsMonitor;
    private readonly TimeProvider _timeProvider;
    private readonly IAppLogger _logger;

    public CatalogProviderRefreshCoordinator(
        ICatalogReadModelRepository repository,
        IBusinessApiClient businessApiClient,
        IOptionsMonitor<CatalogReadModelOptions> optionsMonitor,
        TimeProvider timeProvider,
        IAppLogger logger)
    {
        _repository = repository;
        _businessApiClient = businessApiClient;
        _optionsMonitor = optionsMonitor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task EnsureProviderUsableAsync(
        CatalogProviderReadModel provider,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var now = _timeProvider.GetUtcNow();
        var freshness = CatalogProviderFreshnessState.Create(provider, now, options);

        if (!forceRefresh)
        {
            if (!freshness.HasSnapshot)
            {
                await RefreshProviderAsync(provider, freshness, cancellationToken);
                return;
            }

            if (freshness.IsFresh || freshness.CanServeStale)
            {
                return;
            }
        }

        await RefreshProviderAsync(provider, freshness, cancellationToken);
    }

    private async Task RefreshProviderAsync(
        CatalogProviderReadModel provider,
        CatalogProviderFreshnessState currentFreshness,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var now = _timeProvider.GetUtcNow();
        var leaseToken = Guid.NewGuid().ToString("N");
        var leaseExpiresAtUtc = now.AddSeconds(options.LeaseDurationSeconds);
        var acquired = await _repository.TryAcquireRefreshLeaseAsync(
            provider.Id,
            leaseToken,
            now,
            leaseExpiresAtUtc,
            cancellationToken);

        if (!acquired)
        {
            var providerAfterConcurrentRefresh = await WaitForConcurrentRefreshAsync(
                provider.Id,
                provider.LastSuccessfulRefreshAtUtc,
                cancellationToken);
            var concurrentFreshness = providerAfterConcurrentRefresh is null
                ? CatalogProviderFreshnessState.Empty
                : CatalogProviderFreshnessState.Create(
                    providerAfterConcurrentRefresh,
                    _timeProvider.GetUtcNow(),
                    options);

            if (concurrentFreshness.HasSnapshot && concurrentFreshness.CanServeStale)
            {
                return;
            }

            if (currentFreshness.HasSnapshot && currentFreshness.CanServeStale)
            {
                return;
            }

            throw CreateUnavailable("The catalog provider is being refreshed by another request.");
        }

        try
        {
            var snapshot = await _businessApiClient.GetCatalogSnapshotAsync(
                provider.SourceCompanyId,
                cancellationToken);

            CatalogSnapshotValidator.Validate(provider.SourceCompanyId, snapshot);
            var snapshotHash = CatalogSnapshotHasher.Compute(snapshot);

            if (snapshot.CatalogVersion == provider.CatalogVersion &&
                !string.IsNullOrWhiteSpace(provider.SnapshotHash) &&
                !string.Equals(provider.SnapshotHash, snapshotHash, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    $"Catalog snapshot hash changed without a version increment for provider {provider.Id}.");
            }

            var applyResult = await _repository.ApplySnapshotAsync(
                provider.Id,
                snapshot,
                snapshotHash,
                now,
                leaseToken,
                cancellationToken);

            if (applyResult == CatalogSnapshotApplyResult.VerifiedUnchanged)
            {
                _logger.LogInfo(
                    $"Catalog snapshot verified without content changes for provider {provider.Id} at version {snapshot.CatalogVersion}.");
            }
            else if (applyResult == CatalogSnapshotApplyResult.RejectedVersionRegression)
            {
                _logger.LogWarning(
                    $"Catalog snapshot version regression rejected for provider {provider.Id}. ExistingVersion={provider.CatalogVersion}, IncomingVersion={snapshot.CatalogVersion}.");
            }
            else if (applyResult == CatalogSnapshotApplyResult.LeaseLost)
            {
                _logger.LogWarning(
                    $"Catalog refresh lease was lost before provider {provider.Id} could apply its snapshot.");
                await ThrowWhenNoUsableCacheExistsAsync(provider.Id, cancellationToken);
                return;
            }

            var refreshedProvider = await _repository.GetEnabledProviderSummaryAsync(
                provider.Id,
                cancellationToken);
            var refreshedFreshness = refreshedProvider is null
                ? CatalogProviderFreshnessState.Empty
                : CatalogProviderFreshnessState.Create(
                    refreshedProvider,
                    _timeProvider.GetUtcNow(),
                    options);

            if (!refreshedFreshness.HasSnapshot || !refreshedFreshness.CanServeStale)
            {
                throw CreateUnavailable("The catalog provider could not produce a usable cache.");
            }
        }
        catch (BusinessApiException exception)
        {
            _logger.LogWarning(
                $"Catalog refresh failed for provider {provider.Id} with {exception.GetType().Name}.");
            await ReleaseLeaseSafelyAsync(
                provider.Id,
                leaseToken,
                now,
                "business_api_failure",
                cancellationToken);
            await ThrowWhenNoUsableCacheExistsAsync(provider.Id, cancellationToken);
        }
        catch (CatalogSnapshotValidationException exception)
        {
            _logger.LogWarning(
                $"Catalog snapshot validation failed for provider {provider.Id} with code {exception.Code}.");
            await ReleaseLeaseSafelyAsync(
                provider.Id,
                leaseToken,
                now,
                exception.Code,
                cancellationToken);
            await ThrowWhenNoUsableCacheExistsAsync(provider.Id, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                $"Catalog refresh persistence failed for provider {provider.Id}.",
                exception);
            await ReleaseLeaseSafelyAsync(
                provider.Id,
                leaseToken,
                now,
                "catalog_persistence_failure",
                cancellationToken);
            await ThrowWhenNoUsableCacheExistsAsync(provider.Id, cancellationToken);
        }
    }

    private async Task ThrowWhenNoUsableCacheExistsAsync(
        Guid providerId,
        CancellationToken cancellationToken)
    {
        var provider = await _repository.GetEnabledProviderSummaryAsync(providerId, cancellationToken);
        var freshness = provider is null
            ? CatalogProviderFreshnessState.Empty
            : CatalogProviderFreshnessState.Create(
                provider,
                _timeProvider.GetUtcNow(),
                _optionsMonitor.CurrentValue);

        if (!freshness.HasSnapshot || !freshness.CanServeStale)
        {
            throw CreateUnavailable("The catalog provider does not have a usable cache.");
        }
    }

    private async Task<CatalogProviderReadModel?> WaitForConcurrentRefreshAsync(
        Guid providerId,
        DateTimeOffset? previousLastSuccessfulRefreshAtUtc,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var deadline = _timeProvider.GetUtcNow().AddSeconds(options.LeaseDurationSeconds);

        while (_timeProvider.GetUtcNow() < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            var provider = await _repository.GetEnabledProviderSummaryAsync(providerId, cancellationToken);
            if (provider is null)
            {
                return null;
            }

            if (provider.LastSuccessfulRefreshAtUtc != previousLastSuccessfulRefreshAtUtc ||
                !HasActiveLease(provider, _timeProvider.GetUtcNow()))
            {
                return provider;
            }
        }

        return await _repository.GetEnabledProviderSummaryAsync(providerId, cancellationToken);
    }

    private async Task ReleaseLeaseSafelyAsync(
        Guid providerId,
        string leaseToken,
        DateTimeOffset failedAtUtc,
        string failureCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository.ReleaseRefreshLeaseAsync(
                providerId,
                leaseToken,
                failedAtUtc,
                failureCode,
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                $"Catalog refresh lease release raced for provider {providerId}: {exception.GetType().Name}.");
        }
    }

    private static bool HasActiveLease(
        CatalogProviderReadModel provider,
        DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(provider.RefreshLeaseToken) &&
        provider.RefreshLeaseExpiresAtUtc.HasValue &&
        provider.RefreshLeaseExpiresAtUtc.Value > now;

    private static CatalogReadModelException CreateUnavailable(string message) =>
        new(
            CatalogProblemCodes.Unavailable,
            StatusCodes.Status503ServiceUnavailable,
            message);

    private readonly record struct CatalogProviderFreshnessState(
        bool HasSnapshot,
        bool IsFresh,
        bool CanServeStale,
        DateTimeOffset? FreshUntilUtc,
        DateTimeOffset? MaxStaleUntilUtc)
    {
        public static CatalogProviderFreshnessState Empty => new(
            HasSnapshot: false,
            IsFresh: false,
            CanServeStale: false,
            FreshUntilUtc: null,
            MaxStaleUntilUtc: null);

        public static CatalogProviderFreshnessState Create(
            CatalogProviderReadModel provider,
            DateTimeOffset now,
            CatalogReadModelOptions options)
        {
            if (!provider.LastSuccessfulRefreshAtUtc.HasValue ||
                provider.CatalogVersion <= 0 ||
                string.IsNullOrWhiteSpace(provider.SnapshotHash))
            {
                return Empty;
            }

            var freshUntilUtc = provider.LastSuccessfulRefreshAtUtc.Value
                .AddSeconds(options.FreshWindowSeconds);
            var maxStaleUntilUtc = provider.LastSuccessfulRefreshAtUtc.Value
                .AddSeconds(options.MaxStaleWindowSeconds);

            return new CatalogProviderFreshnessState(
                HasSnapshot: true,
                IsFresh: now <= freshUntilUtc,
                CanServeStale: now <= maxStaleUntilUtc,
                FreshUntilUtc: freshUntilUtc,
                MaxStaleUntilUtc: maxStaleUntilUtc);
        }
    }
}
