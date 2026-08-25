using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Catalog;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace GhseeliApis.Repositories;

public sealed class CatalogReadModelRepository : ICatalogReadModelRepository
{
    private const string GraphQueryTag =
        "CatalogReadModelRepository.GetEnabledProvidersWithGraphAsync";
    private static readonly JsonSerializerOptions JsonOptions =
        Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract.CreateJsonSerializerOptions();

    private readonly ApplicationDbContext _context;

    public CatalogReadModelRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task SynchronizeConfiguredProvidersAsync(
        IReadOnlyCollection<CatalogProviderRegistrationOptions> providers,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            await ExecuteInTransactionAsync(
                innerCancellationToken => SynchronizeConfiguredProvidersCoreAsync(
                    providers,
                    innerCancellationToken),
                IsolationLevel.Serializable,
                cancellationToken);
            return;
        }

        await SynchronizeConfiguredProvidersCoreAsync(providers, cancellationToken);
    }

    private async Task SynchronizeConfiguredProvidersCoreAsync(
        IReadOnlyCollection<CatalogProviderRegistrationOptions> providers,
        CancellationToken cancellationToken)
    {
        var configuredProviders = providers
            .OrderBy(provider => provider.Order)
            .ThenBy(provider => provider.SourceCompanyId)
            .ToArray();

        var existingProviders = await _context.CatalogProviders
            .OrderBy(provider => provider.DisplayOrder)
            .ThenBy(provider => provider.SourceCompanyId)
            .ToListAsync(cancellationToken);

        var existingBySourceId = existingProviders.ToDictionary(provider => provider.SourceCompanyId);
        var configuredSourceIds = new HashSet<Guid>(
            configuredProviders.Select(provider => provider.SourceCompanyId));
        var hasChanges = false;

        foreach (var configuredProvider in configuredProviders)
        {
            if (existingBySourceId.TryGetValue(configuredProvider.SourceCompanyId, out var existingProvider))
            {
                if (existingProvider.IsEnabled != configuredProvider.Enabled ||
                    existingProvider.DisplayOrder != configuredProvider.Order)
                {
                    existingProvider.IsEnabled = configuredProvider.Enabled;
                    existingProvider.DisplayOrder = configuredProvider.Order;
                    hasChanges = true;
                }

                continue;
            }

            _context.CatalogProviders.Add(new CatalogProviderReadModel
            {
                Id = Guid.NewGuid(),
                SourceCompanyId = configuredProvider.SourceCompanyId,
                IsEnabled = configuredProvider.Enabled,
                DisplayOrder = configuredProvider.Order
            });
            hasChanges = true;
        }

        foreach (var existingProvider in existingProviders)
        {
            if (!configuredSourceIds.Contains(existingProvider.SourceCompanyId) &&
                existingProvider.IsEnabled)
            {
                existingProvider.IsEnabled = false;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CatalogProviderReadModel>> ListEnabledProviderSummariesAsync(
        CancellationToken cancellationToken)
    {
        return await _context.CatalogProviders
            .AsNoTracking()
            .Where(provider =>
                provider.IsEnabled &&
                provider.BusinessVerticalCode == BusinessVerticalSnapshotDefaults.CarWashCode)
            .OrderBy(provider => provider.DisplayOrder)
            .ThenBy(provider => provider.NameAr)
            .ToListAsync(cancellationToken);
    }

    public Task<CatalogProviderReadModel?> GetEnabledProviderSummaryAsync(
        Guid providerId,
        CancellationToken cancellationToken) =>
        _context.CatalogProviders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                provider =>
                    provider.Id == providerId &&
                    provider.IsEnabled &&
                    provider.BusinessVerticalCode == BusinessVerticalSnapshotDefaults.CarWashCode,
                cancellationToken);

    public Task<CatalogProviderReadModel?> GetEnabledProviderBySourceCompanyIdWithGraphAsync(
        Guid sourceCompanyId,
        CancellationToken cancellationToken) =>
        _context.CatalogProviders
            .AsNoTracking()
            .AsSingleQuery()
            .Where(provider =>
                provider.IsEnabled &&
                provider.BusinessVerticalCode == BusinessVerticalSnapshotDefaults.CarWashCode &&
                provider.SourceCompanyId == sourceCompanyId)
            .Include(provider => provider.Branches)
            .Include(provider => provider.Categories)
                .ThenInclude(category => category.Offerings)
                    .ThenInclude(offering => offering.Branch)
            .Include(provider => provider.Categories)
                .ThenInclude(category => category.Offerings)
                    .ThenInclude(offering => offering.AddonGroups)
                        .ThenInclude(addonGroup => addonGroup.Choices)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<CatalogBranchReadModel?> GetEnabledBranchSummaryAsync(
        Guid branchId,
        CancellationToken cancellationToken) =>
        _context.CatalogBranches
            .AsNoTracking()
            .SingleOrDefaultAsync(
                branch =>
                    branch.Id == branchId &&
                    branch.Provider.IsEnabled &&
                    branch.Provider.BusinessVerticalCode ==
                        BusinessVerticalSnapshotDefaults.CarWashCode,
                cancellationToken);

    public Task<CatalogCategoryReadModel?> GetEnabledCategorySummaryAsync(
        Guid categoryId,
        CancellationToken cancellationToken) =>
        _context.CatalogCategories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                category =>
                    category.Id == categoryId &&
                    category.Provider.IsEnabled &&
                    category.Provider.BusinessVerticalCode ==
                        BusinessVerticalSnapshotDefaults.CarWashCode,
                cancellationToken);

    public Task<CatalogOfferingReadModel?> GetEnabledOfferingSummaryAsync(
        Guid offeringId,
        CancellationToken cancellationToken) =>
        _context.CatalogOfferings
            .AsNoTracking()
            .Include(offering => offering.Category)
            .SingleOrDefaultAsync(
                offering =>
                    offering.Id == offeringId &&
                    offering.Category.Provider.IsEnabled &&
                    offering.Category.Provider.BusinessVerticalCode ==
                        BusinessVerticalSnapshotDefaults.CarWashCode,
                cancellationToken);

    public async Task<IReadOnlyList<CatalogProviderReadModel>> GetEnabledProvidersWithGraphAsync(
        IReadOnlyCollection<Guid>? providerIds,
        CancellationToken cancellationToken)
    {
        if (providerIds is not null && providerIds.Count == 0)
        {
            return Array.Empty<CatalogProviderReadModel>();
        }

        var query = _context.CatalogProviders
            .AsNoTracking()
            .AsSingleQuery()
            .TagWith(GraphQueryTag)
            .Where(provider =>
                provider.IsEnabled &&
                provider.BusinessVerticalCode == BusinessVerticalSnapshotDefaults.CarWashCode);

        if (providerIds is not null)
        {
            query = query.Where(provider => providerIds.Contains(provider.Id));
        }

        return await query
            .Include(provider => provider.Branches)
            .Include(provider => provider.Categories)
                .ThenInclude(category => category.Offerings)
                    .ThenInclude(offering => offering.Branch)
            .Include(provider => provider.Categories)
                .ThenInclude(category => category.Offerings)
                    .ThenInclude(offering => offering.AddonGroups)
                        .ThenInclude(addonGroup => addonGroup.Choices)
            .OrderBy(provider => provider.DisplayOrder)
            .ThenBy(provider => provider.NameAr)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryAcquireRefreshLeaseAsync(
        Guid providerId,
        string leaseToken,
        DateTimeOffset acquiredAtUtc,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var provider = await _context.CatalogProviders
            .SingleOrDefaultAsync(item => item.Id == providerId, cancellationToken);

        if (provider is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(provider.RefreshLeaseToken) &&
            provider.RefreshLeaseExpiresAtUtc.HasValue &&
            provider.RefreshLeaseExpiresAtUtc.Value > acquiredAtUtc)
        {
            return false;
        }

        provider.RefreshLeaseToken = leaseToken;
        provider.RefreshLeaseAcquiredAtUtc = acquiredAtUtc;
        provider.RefreshLeaseExpiresAtUtc = expiresAtUtc;
        provider.LastAttemptedRefreshAtUtc = acquiredAtUtc;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task ReleaseRefreshLeaseAsync(
        Guid providerId,
        string leaseToken,
        DateTimeOffset failedAtUtc,
        string failureCode,
        CancellationToken cancellationToken)
    {
        var provider = await _context.CatalogProviders
            .SingleOrDefaultAsync(item => item.Id == providerId, cancellationToken);

        if (provider is null ||
            !string.Equals(provider.RefreshLeaseToken, leaseToken, StringComparison.Ordinal))
        {
            return;
        }

        provider.RefreshLeaseToken = null;
        provider.RefreshLeaseAcquiredAtUtc = null;
        provider.RefreshLeaseExpiresAtUtc = null;
        provider.LastFailedRefreshAtUtc = failedAtUtc;
        provider.LastFailureCode = failureCode;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CatalogSnapshotApplyResult> ApplySnapshotAsync(
        Guid providerId,
        CatalogSnapshotResponse snapshot,
        string snapshotHash,
        DateTimeOffset refreshedAtUtc,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        return await ExecuteInTransactionAsync(
            innerCancellationToken => ApplySnapshotCoreAsync(
                providerId,
                snapshot,
                snapshotHash,
                refreshedAtUtc,
                leaseToken,
                innerCancellationToken),
            IsolationLevel.ReadCommitted,
            cancellationToken);
    }

    private async Task<CatalogSnapshotApplyResult> ApplySnapshotCoreAsync(
        Guid providerId,
        CatalogSnapshotResponse snapshot,
        string snapshotHash,
        DateTimeOffset refreshedAtUtc,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        var provider = await _context.CatalogProviders
            .Include(item => item.Branches)
            .Include(item => item.Categories)
                .ThenInclude(category => category.Offerings)
                    .ThenInclude(offering => offering.AddonGroups)
                        .ThenInclude(addonGroup => addonGroup.Choices)
            .SingleOrDefaultAsync(item => item.Id == providerId, cancellationToken);

        if (provider is null ||
            !string.Equals(provider.RefreshLeaseToken, leaseToken, StringComparison.Ordinal))
        {
            return CatalogSnapshotApplyResult.LeaseLost;
        }

        if (snapshot.CatalogVersion < provider.CatalogVersion)
        {
            provider.RefreshLeaseToken = null;
            provider.RefreshLeaseAcquiredAtUtc = null;
            provider.RefreshLeaseExpiresAtUtc = null;
            provider.LastFailedRefreshAtUtc = refreshedAtUtc;
            provider.LastFailureCode = "catalog_version_regression";
            await _context.SaveChangesAsync(cancellationToken);

            return CatalogSnapshotApplyResult.RejectedVersionRegression;
        }

        provider.NameAr = ConfigurationTextNormalizer.NormalizeRequired(snapshot.Company.NameAr);
        provider.NameHe = ConfigurationTextNormalizer.NormalizeOptional(snapshot.Company.NameHe);
        provider.DescriptionAr = ConfigurationTextNormalizer.NormalizeOptional(snapshot.Company.DescriptionAr);
        provider.DescriptionHe = ConfigurationTextNormalizer.NormalizeOptional(snapshot.Company.DescriptionHe);
        provider.Phone = ConfigurationTextNormalizer.NormalizeOptional(snapshot.Company.Phone);
        provider.SnapshotGeneratedAtUtc = new DateTimeOffset(
            DateTime.SpecifyKind(snapshot.GeneratedAtUtc, DateTimeKind.Utc));
        provider.LastSuccessfulRefreshAtUtc = refreshedAtUtc;
        provider.LastFailedRefreshAtUtc = null;
        provider.LastFailureCode = null;

        if (snapshot.CatalogVersion == provider.CatalogVersion &&
            string.Equals(provider.SnapshotHash, snapshotHash, StringComparison.Ordinal))
        {
            provider.RefreshLeaseToken = null;
            provider.RefreshLeaseAcquiredAtUtc = null;
            provider.RefreshLeaseExpiresAtUtc = null;
            await _context.SaveChangesAsync(cancellationToken);

            return CatalogSnapshotApplyResult.VerifiedUnchanged;
        }

        provider.CatalogVersion = snapshot.CatalogVersion;
        provider.SnapshotHash = snapshotHash;

        var serviceAreasByBranchId = snapshot.ServiceAreas
            .ToDictionary(serviceArea => serviceArea.BranchId);
        var existingBranches = provider.Branches
            .ToDictionary(branch => branch.SourceBranchId);
        var existingCategories = provider.Categories
            .ToDictionary(category => category.SourceCategoryId);
        var existingOfferings = provider.Categories
            .SelectMany(category => category.Offerings)
            .ToDictionary(offering => offering.SourceOfferingId);
        var existingAddonGroups = existingOfferings.Values
            .SelectMany(offering => offering.AddonGroups)
            .ToDictionary(addonGroup => addonGroup.SourceAddonGroupId);
        var existingAddonChoices = existingAddonGroups.Values
            .SelectMany(addonGroup => addonGroup.Choices)
            .ToDictionary(addonChoice => addonChoice.SourceAddonChoiceId);

        var seenBranchIds = new HashSet<Guid>();
        var seenCategoryIds = new HashSet<Guid>();
        var seenOfferingIds = new HashSet<Guid>();
        var seenAddonGroupIds = new HashSet<Guid>();
        var seenAddonChoiceIds = new HashSet<Guid>();

        var branchEntities = new Dictionary<Guid, CatalogBranchReadModel>();
        var branchDisplayOrder = 0;

        foreach (var snapshotBranch in snapshot.Branches)
        {
            if (!existingBranches.TryGetValue(snapshotBranch.Id, out var branch))
            {
                branch = new CatalogBranchReadModel
                {
                    Id = Guid.NewGuid(),
                    SourceBranchId = snapshotBranch.Id,
                    ProviderId = provider.Id
                };
                branch.Provider = provider;
                _context.CatalogBranches.Add(branch);
                existingBranches[snapshotBranch.Id] = branch;
            }

            branch.NameAr = ConfigurationTextNormalizer.NormalizeRequired(snapshotBranch.NameAr);
            branch.NameHe = ConfigurationTextNormalizer.NormalizeOptional(snapshotBranch.NameHe);
            branch.AddressAr = ConfigurationTextNormalizer.NormalizeRequired(snapshotBranch.AddressAr);
            branch.AddressHe = ConfigurationTextNormalizer.NormalizeOptional(snapshotBranch.AddressHe);
            branch.Latitude = snapshotBranch.Latitude;
            branch.Longitude = snapshotBranch.Longitude;
            branch.DisplayOrder = branchDisplayOrder++;

            if (serviceAreasByBranchId.TryGetValue(snapshotBranch.Id, out var serviceArea))
            {
                branch.HasPublishedServiceArea = serviceArea.IsActive;
                branch.UsesBranchCoordinates = serviceArea.UsesBranchCoordinates;
                branch.ServiceAreaCenterLatitude = serviceArea.CenterLatitude;
                branch.ServiceAreaCenterLongitude = serviceArea.CenterLongitude;
                branch.ServiceAreaRadiusKm = serviceArea.RadiusKm;
            }
            else
            {
                branch.HasPublishedServiceArea = false;
                branch.UsesBranchCoordinates = false;
                branch.ServiceAreaCenterLatitude = null;
                branch.ServiceAreaCenterLongitude = null;
                branch.ServiceAreaRadiusKm = null;
            }

            branch.AvailabilitySnapshotJson = snapshotBranch.Availability is null
                ? null
                : JsonSerializer.Serialize(snapshotBranch.Availability, JsonOptions);

            branchEntities[snapshotBranch.Id] = branch;
            seenBranchIds.Add(snapshotBranch.Id);
        }

        foreach (var snapshotCategory in snapshot.Categories)
        {
            if (!existingCategories.TryGetValue(snapshotCategory.Id, out var category))
            {
                category = new CatalogCategoryReadModel
                {
                    Id = Guid.NewGuid(),
                    SourceCategoryId = snapshotCategory.Id,
                    ProviderId = provider.Id
                };
                category.Provider = provider;
                _context.CatalogCategories.Add(category);
                existingCategories[snapshotCategory.Id] = category;
            }

            category.NameAr = ConfigurationTextNormalizer.NormalizeRequired(snapshotCategory.NameAr);
            category.NameHe = ConfigurationTextNormalizer.NormalizeOptional(snapshotCategory.NameHe);
            category.DescriptionAr = ConfigurationTextNormalizer.NormalizeOptional(snapshotCategory.DescriptionAr);
            category.DescriptionHe = ConfigurationTextNormalizer.NormalizeOptional(snapshotCategory.DescriptionHe);
            category.DisplayOrder = snapshotCategory.DisplayOrder;
            seenCategoryIds.Add(snapshotCategory.Id);

            foreach (var snapshotOffering in snapshotCategory.Offerings)
            {
                if (!existingOfferings.TryGetValue(snapshotOffering.Id, out var offering))
                {
                    offering = new CatalogOfferingReadModel
                    {
                        Id = Guid.NewGuid(),
                        SourceOfferingId = snapshotOffering.Id
                    };
                    _context.CatalogOfferings.Add(offering);
                    existingOfferings[snapshotOffering.Id] = offering;
                }

                offering.Category = category;
                offering.CategoryId = category.Id;
                offering.Branch = snapshotOffering.BranchId.HasValue
                    ? branchEntities[snapshotOffering.BranchId.Value]
                    : null;
                offering.BranchId = offering.Branch?.Id;
                offering.NameAr = ConfigurationTextNormalizer.NormalizeRequired(snapshotOffering.NameAr);
                offering.NameHe = ConfigurationTextNormalizer.NormalizeOptional(snapshotOffering.NameHe);
                offering.DescriptionAr = ConfigurationTextNormalizer.NormalizeOptional(snapshotOffering.DescriptionAr);
                offering.DescriptionHe = ConfigurationTextNormalizer.NormalizeOptional(snapshotOffering.DescriptionHe);
                offering.BasePrice = snapshotOffering.BasePrice;
                offering.DurationMinutes = snapshotOffering.DurationMinutes;
                offering.ImageUrl = ConfigurationTextNormalizer.NormalizeOptional(snapshotOffering.ImageUrl);
                offering.ReferenceCode = ConfigurationTextNormalizer.NormalizeOptional(snapshotOffering.ReferenceCode);
                offering.DisplayOrder = snapshotOffering.DisplayOrder;
                seenOfferingIds.Add(snapshotOffering.Id);

                foreach (var snapshotAddonGroup in snapshotOffering.AddonGroups)
                {
                    if (!existingAddonGroups.TryGetValue(snapshotAddonGroup.Id, out var addonGroup))
                    {
                        addonGroup = new CatalogAddonGroupReadModel
                        {
                            Id = Guid.NewGuid(),
                            SourceAddonGroupId = snapshotAddonGroup.Id
                        };
                        _context.CatalogAddonGroups.Add(addonGroup);
                        existingAddonGroups[snapshotAddonGroup.Id] = addonGroup;
                    }

                    addonGroup.Offering = offering;
                    addonGroup.OfferingId = offering.Id;
                    addonGroup.NameAr = ConfigurationTextNormalizer.NormalizeRequired(snapshotAddonGroup.NameAr);
                    addonGroup.NameHe = ConfigurationTextNormalizer.NormalizeOptional(snapshotAddonGroup.NameHe);
                    addonGroup.DescriptionAr = ConfigurationTextNormalizer.NormalizeOptional(snapshotAddonGroup.DescriptionAr);
                    addonGroup.DescriptionHe = ConfigurationTextNormalizer.NormalizeOptional(snapshotAddonGroup.DescriptionHe);
                    addonGroup.SelectionType = snapshotAddonGroup.SelectionType;
                    addonGroup.IsRequired = snapshotAddonGroup.IsRequired;
                    addonGroup.MinimumSelections = snapshotAddonGroup.MinimumSelections;
                    addonGroup.MaximumSelections = snapshotAddonGroup.MaximumSelections;
                    addonGroup.DisplayOrder = snapshotAddonGroup.DisplayOrder;
                    seenAddonGroupIds.Add(snapshotAddonGroup.Id);

                    foreach (var snapshotAddonChoice in snapshotAddonGroup.Choices)
                    {
                        if (!existingAddonChoices.TryGetValue(snapshotAddonChoice.Id, out var addonChoice))
                        {
                            addonChoice = new CatalogAddonChoiceReadModel
                            {
                                Id = Guid.NewGuid(),
                                SourceAddonChoiceId = snapshotAddonChoice.Id
                            };
                            _context.CatalogAddonChoices.Add(addonChoice);
                            existingAddonChoices[snapshotAddonChoice.Id] = addonChoice;
                        }

                        addonChoice.AddonGroup = addonGroup;
                        addonChoice.AddonGroupId = addonGroup.Id;
                        addonChoice.NameAr = ConfigurationTextNormalizer.NormalizeRequired(snapshotAddonChoice.NameAr);
                        addonChoice.NameHe = ConfigurationTextNormalizer.NormalizeOptional(snapshotAddonChoice.NameHe);
                        addonChoice.DescriptionAr = ConfigurationTextNormalizer.NormalizeOptional(snapshotAddonChoice.DescriptionAr);
                        addonChoice.DescriptionHe = ConfigurationTextNormalizer.NormalizeOptional(snapshotAddonChoice.DescriptionHe);
                        addonChoice.PriceAdjustment = snapshotAddonChoice.PriceAdjustment;
                        addonChoice.DurationAdjustmentMinutes = snapshotAddonChoice.DurationAdjustmentMinutes;
                        addonChoice.DefaultQuantity = snapshotAddonChoice.DefaultQuantity;
                        addonChoice.DisplayOrder = snapshotAddonChoice.DisplayOrder;
                        seenAddonChoiceIds.Add(snapshotAddonChoice.Id);
                    }
                }
            }
        }

        _context.CatalogAddonChoices.RemoveRange(existingAddonChoices.Values
            .Where(addonChoice => !seenAddonChoiceIds.Contains(addonChoice.SourceAddonChoiceId))
            .ToArray());
        _context.CatalogAddonGroups.RemoveRange(existingAddonGroups.Values
            .Where(addonGroup => !seenAddonGroupIds.Contains(addonGroup.SourceAddonGroupId))
            .ToArray());
        _context.CatalogOfferings.RemoveRange(existingOfferings.Values
            .Where(offering => !seenOfferingIds.Contains(offering.SourceOfferingId))
            .ToArray());
        _context.CatalogCategories.RemoveRange(existingCategories.Values
            .Where(category => !seenCategoryIds.Contains(category.SourceCategoryId))
            .ToArray());
        _context.CatalogBranches.RemoveRange(existingBranches.Values
            .Where(branch => !seenBranchIds.Contains(branch.SourceBranchId))
            .ToArray());

        provider.RefreshLeaseToken = null;
        provider.RefreshLeaseAcquiredAtUtc = null;
        provider.RefreshLeaseExpiresAtUtc = null;

        await _context.SaveChangesAsync(cancellationToken);

        return CatalogSnapshotApplyResult.Applied;
    }

    private async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        await ExecuteInTransactionAsync(
            async innerCancellationToken =>
            {
                await operation(innerCancellationToken);
                return true;
            },
            isolationLevel,
            cancellationToken);
    }

    private async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            _context.ChangeTracker.Clear();
            var result = await operation(cancellationToken);
            _context.ChangeTracker.Clear();
            return result;
        }

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(
                isolationLevel,
                cancellationToken);

            try
            {
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                throw;
            }
        });
    }
}
