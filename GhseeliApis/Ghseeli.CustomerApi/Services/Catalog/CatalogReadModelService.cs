using FluentValidation.Results;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace GhseeliApis.Services.Catalog;

public interface ICatalogReadModelService
{
    Task<CatalogCategoriesResponse> GetCategoriesAsync(
        GetCatalogCategoriesRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);

    Task<CatalogBusinessesResponse> GetBusinessesAsync(
        GetCatalogBusinessesRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);

    Task<CatalogBusinessDetailResponse> GetBusinessAsync(
        Guid businessId,
        GetCatalogResourceRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);

    Task<CatalogBusinessOfferingsResponse> GetBusinessOfferingsAsync(
        Guid businessId,
        GetCatalogBusinessOfferingsRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);

    Task<CatalogOfferingDetailResponse> GetOfferingAsync(
        Guid offeringId,
        GetCatalogResourceRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);
}

public sealed class CatalogReadModelException : Exception
{
    public CatalogReadModelException(string code, int statusCode, string message)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public sealed class CatalogReadModelService : ICatalogReadModelService
{
    private readonly ICatalogReadModelRepository _repository;
    private readonly IBusinessApiClient _businessApiClient;
    private readonly ICatalogProviderRefreshCoordinator _refreshCoordinator;
    private readonly IOptionsMonitor<CatalogReadModelOptions> _optionsMonitor;
    private readonly TimeProvider _timeProvider;
    private readonly IAppLogger _logger;

    public CatalogReadModelService(
        ICatalogReadModelRepository repository,
        IBusinessApiClient businessApiClient,
        ICatalogProviderRefreshCoordinator refreshCoordinator,
        IOptionsMonitor<CatalogReadModelOptions> optionsMonitor,
        TimeProvider timeProvider,
        IAppLogger logger)
    {
        _repository = repository;
        _businessApiClient = businessApiClient;
        _refreshCoordinator = refreshCoordinator;
        _optionsMonitor = optionsMonitor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<CatalogCategoriesResponse> GetCategoriesAsync(
        GetCatalogCategoriesRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ResolveLanguage(request.Language, acceptLanguageHeader);
        await SynchronizeConfiguredProvidersAsync(cancellationToken);

        IReadOnlyCollection<Guid> providerIds;
        if (request.BusinessId.HasValue)
        {
            var providerSummary = await _repository.GetEnabledProviderSummaryAsync(
                request.BusinessId.Value,
                cancellationToken);
            if (providerSummary is null)
            {
                throw CreateBusinessNotFound();
            }

            await EnsureProviderUsableAsync(providerSummary, request.Refresh, cancellationToken);
            providerIds = [providerSummary.Id];
        }
        else
        {
            var providers = await _repository.ListEnabledProviderSummariesAsync(cancellationToken);
            if (providers.Count == 0)
            {
                return new CatalogCategoriesResponse
                {
                    Language = language,
                    Categories = Array.Empty<CatalogCategoryResponse>()
                };
            }

            foreach (var provider in providers)
            {
                await EnsureProviderUsableAsync(provider, request.Refresh, cancellationToken);
            }

            providerIds = providers.Select(provider => provider.Id).ToArray();
        }

        var providerGraphs = await _repository.GetEnabledProvidersWithGraphAsync(
            providerIds,
            cancellationToken);
        if (request.BusinessId.HasValue && providerGraphs.Count == 0)
        {
            throw CreateBusinessNotFound();
        }

        var categories = providerGraphs
            .SelectMany(provider => provider.Categories
                .Where(category => GetEligibleOfferings(provider, category.Id, null).Any())
                .OrderBy(category => category.DisplayOrder)
                .ThenBy(category => category.NameAr)
                .Select(category => MapCategory(provider, category, language)))
            .ToArray();

        return new CatalogCategoriesResponse
        {
            Language = language,
            Categories = categories
        };
    }

    public async Task<CatalogBusinessesResponse> GetBusinessesAsync(
        GetCatalogBusinessesRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ResolveLanguage(request.Language, acceptLanguageHeader);
        await SynchronizeConfiguredProvidersAsync(cancellationToken);

        var branchSummary = request.BranchId.HasValue
            ? await _repository.GetEnabledBranchSummaryAsync(request.BranchId.Value, cancellationToken)
            : null;
        var categorySummary = request.CategoryId.HasValue
            ? await _repository.GetEnabledCategorySummaryAsync(request.CategoryId.Value, cancellationToken)
            : null;

        ValidateCompatibleProviderScope(
            branchSummary?.ProviderId,
            categorySummary?.ProviderId,
            branchSummary is not null || !request.BranchId.HasValue,
            categorySummary is not null || !request.CategoryId.HasValue);

        IReadOnlyCollection<Guid> providerIds;
        if (branchSummary is not null)
        {
            providerIds = [branchSummary.ProviderId];
        }
        else if (categorySummary is not null)
        {
            providerIds = [categorySummary.ProviderId];
        }
        else
        {
            var providers = await _repository.ListEnabledProviderSummariesAsync(cancellationToken);
            if (providers.Count == 0)
            {
                return new CatalogBusinessesResponse
                {
                    Language = language,
                    Businesses = Array.Empty<CatalogBusinessResponse>()
                };
            }

            foreach (var provider in providers)
            {
                await EnsureProviderUsableAsync(provider, request.Refresh, cancellationToken);
            }

            providerIds = providers.Select(provider => provider.Id).ToArray();
        }

        if (branchSummary is not null)
        {
            var providerSummary = await _repository.GetEnabledProviderSummaryAsync(
                branchSummary.ProviderId,
                cancellationToken);
            if (providerSummary is null)
            {
                throw CreateUnavailable("The branch provider is unavailable.");
            }

            await EnsureProviderUsableAsync(providerSummary, request.Refresh, cancellationToken);
        }
        else if (categorySummary is not null)
        {
            var providerSummary = await _repository.GetEnabledProviderSummaryAsync(
                categorySummary.ProviderId,
                cancellationToken);
            if (providerSummary is null)
            {
                throw CreateUnavailable("The category provider is unavailable.");
            }

            await EnsureProviderUsableAsync(providerSummary, request.Refresh, cancellationToken);
        }

        var providersWithGraph = await _repository.GetEnabledProvidersWithGraphAsync(
            providerIds,
            cancellationToken);
        if (branchSummary is not null && providersWithGraph.Count == 0)
        {
            throw CreateUnavailable("The branch provider is unavailable.");
        }

        if (categorySummary is not null && providersWithGraph.Count == 0)
        {
            throw CreateUnavailable("The category provider is unavailable.");
        }

        var businesses = providersWithGraph
            .Select(provider => CreateBusinessProjection(
                provider,
                language,
                request.BranchId,
                request.CategoryId))
            .Where(projection => projection is not null)
            .Select(projection => projection!)
            .OrderBy(projection => projection.Provider.DisplayOrder)
            .ThenBy(projection => projection.Provider.NameAr)
            .Select(projection => MapBusiness(
                projection.Provider,
                language,
                projection.VisibleBranches))
            .ToArray();

        return new CatalogBusinessesResponse
        {
            Language = language,
            Businesses = businesses
        };
    }

    public async Task<CatalogBusinessDetailResponse> GetBusinessAsync(
        Guid businessId,
        GetCatalogResourceRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ResolveLanguage(request.Language, acceptLanguageHeader);
        await SynchronizeConfiguredProvidersAsync(cancellationToken);

        var providerSummary = await _repository.GetEnabledProviderSummaryAsync(
            businessId,
            cancellationToken);
        if (providerSummary is null)
        {
            throw CreateBusinessNotFound();
        }

        await EnsureProviderUsableAsync(providerSummary, request.Refresh, cancellationToken);
        var provider = (await _repository.GetEnabledProvidersWithGraphAsync(
            [providerSummary.Id],
            cancellationToken)).SingleOrDefault();
        if (provider is null)
        {
            throw CreateBusinessNotFound();
        }

        var visibleBranches = SelectVisibleBranches(provider, categoryId: null, branchId: null);
        if (visibleBranches.Length == 0 || !GetEligibleOfferings(provider, null, null).Any())
        {
            throw CreateBusinessNotFound();
        }

        return new CatalogBusinessDetailResponse
        {
            Language = language,
            Business = MapBusiness(provider, language, visibleBranches)
        };
    }

    public async Task<CatalogBusinessOfferingsResponse> GetBusinessOfferingsAsync(
        Guid businessId,
        GetCatalogBusinessOfferingsRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ResolveLanguage(request.Language, acceptLanguageHeader);
        await SynchronizeConfiguredProvidersAsync(cancellationToken);

        var providerSummary = await _repository.GetEnabledProviderSummaryAsync(
            businessId,
            cancellationToken);
        if (providerSummary is null)
        {
            throw CreateBusinessNotFound();
        }

        var branchSummary = request.BranchId.HasValue
            ? await _repository.GetEnabledBranchSummaryAsync(request.BranchId.Value, cancellationToken)
            : null;
        var categorySummary = request.CategoryId.HasValue
            ? await _repository.GetEnabledCategorySummaryAsync(request.CategoryId.Value, cancellationToken)
            : null;

        ValidateCompatibleProviderScope(
            branchSummary?.ProviderId,
            categorySummary?.ProviderId,
            branchSummary is not null || !request.BranchId.HasValue,
            categorySummary is not null || !request.CategoryId.HasValue);

        if ((branchSummary is not null && branchSummary.ProviderId != providerSummary.Id) ||
            (categorySummary is not null && categorySummary.ProviderId != providerSummary.Id) ||
            (request.BranchId.HasValue && branchSummary is null) ||
            (request.CategoryId.HasValue && categorySummary is null))
        {
            throw CreateFilterMismatch();
        }

        await EnsureProviderUsableAsync(providerSummary, request.Refresh, cancellationToken);
        var provider = (await _repository.GetEnabledProvidersWithGraphAsync(
            [providerSummary.Id],
            cancellationToken)).SingleOrDefault();
        if (provider is null)
        {
            throw CreateBusinessNotFound();
        }

        if (!IsValidBusinessFilterScope(provider, request.BranchId, request.CategoryId))
        {
            throw CreateFilterMismatch();
        }

        var offerings = GetEligibleOfferings(provider, request.CategoryId, request.BranchId)
            .OrderBy(offering => offering.Category.DisplayOrder)
            .ThenBy(offering => offering.DisplayOrder)
            .ThenBy(offering => offering.NameAr)
            .Select(offering => MapOffering(provider, offering, language))
            .ToArray();
        var visibleBranches = SelectVisibleBranches(provider, request.CategoryId, request.BranchId);

        return new CatalogBusinessOfferingsResponse
        {
            Language = language,
            Business = MapBusiness(provider, language, visibleBranches),
            Offerings = offerings
        };
    }

    public async Task<CatalogOfferingDetailResponse> GetOfferingAsync(
        Guid offeringId,
        GetCatalogResourceRequest request,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ResolveLanguage(request.Language, acceptLanguageHeader);
        await SynchronizeConfiguredProvidersAsync(cancellationToken);

        var offeringSummary = await _repository.GetEnabledOfferingSummaryAsync(
            offeringId,
            cancellationToken);
        if (offeringSummary is null)
        {
            throw CreateOfferingNotFound();
        }

        var providerSummary = await _repository.GetEnabledProviderSummaryAsync(
            offeringSummary.Category.ProviderId,
            cancellationToken);
        if (providerSummary is null)
        {
            throw CreateOfferingNotFound();
        }

        await EnsureProviderUsableAsync(providerSummary, request.Refresh, cancellationToken);
        var provider = (await _repository.GetEnabledProvidersWithGraphAsync(
            [providerSummary.Id],
            cancellationToken)).SingleOrDefault();
        if (provider is null)
        {
            throw CreateOfferingNotFound();
        }

        var offering = GetEligibleOfferings(provider, null, null)
            .SingleOrDefault(item => item.Id == offeringId);

        if (offering is null)
        {
            throw CreateOfferingNotFound();
        }

        return new CatalogOfferingDetailResponse
        {
            Language = language,
            Offering = MapOffering(provider, offering, language)
        };
    }

    private async Task SynchronizeConfiguredProvidersAsync(CancellationToken cancellationToken)
    {
        await _repository.SynchronizeConfiguredProvidersAsync(
            _optionsMonitor.CurrentValue.Providers,
            cancellationToken);
    }

    private Task EnsureProviderUsableAsync(
        CatalogProviderReadModel provider,
        bool forceRefresh,
        CancellationToken cancellationToken) =>
        _refreshCoordinator.EnsureProviderUsableAsync(provider, forceRefresh, cancellationToken);

    private void ValidateCompatibleProviderScope(
        Guid? branchProviderId,
        Guid? categoryProviderId,
        bool branchResolved,
        bool categoryResolved)
    {
        if (!branchResolved || !categoryResolved)
        {
            throw CreateFilterMismatch();
        }

        if (branchProviderId.HasValue &&
            categoryProviderId.HasValue &&
            branchProviderId.Value != categoryProviderId.Value)
        {
            throw CreateFilterMismatch();
        }
    }

    private static bool IsValidBusinessFilterScope(
        CatalogProviderReadModel provider,
        Guid? branchId,
        Guid? categoryId)
    {
        if (branchId.HasValue &&
            !provider.Branches.Any(branch => branch.Id == branchId.Value && IsEligibleBranch(branch)))
        {
            return false;
        }

        if (categoryId.HasValue &&
            !GetEligibleOfferings(provider, categoryId.Value, null).Any())
        {
            return false;
        }

        return true;
    }

    private static CatalogBusinessProjection? CreateBusinessProjection(
        CatalogProviderReadModel provider,
        string language,
        Guid? branchId,
        Guid? categoryId)
    {
        var visibleBranches = SelectVisibleBranches(provider, categoryId, branchId);
        var visibleOfferings = GetEligibleOfferings(provider, categoryId, branchId).ToArray();

        if (visibleBranches.Length == 0 || visibleOfferings.Length == 0)
        {
            return null;
        }

        return new CatalogBusinessProjection(provider, visibleBranches, visibleOfferings);
    }

    private static CatalogBranchReadModel[] SelectVisibleBranches(
        CatalogProviderReadModel provider,
        Guid? categoryId,
        Guid? branchId)
    {
        var eligibleBranches = provider.Branches
            .Where(IsEligibleBranch)
            .OrderBy(branch => branch.DisplayOrder)
            .ThenBy(branch => branch.NameAr)
            .ToArray();

        if (branchId.HasValue)
        {
            return eligibleBranches
                .Where(branch => branch.Id == branchId.Value)
                .ToArray();
        }

        var offerings = GetEligibleOfferings(provider, categoryId, branchId).ToArray();
        if (offerings.Length == 0)
        {
            return Array.Empty<CatalogBranchReadModel>();
        }

        if (offerings.Any(offering => !offering.BranchId.HasValue))
        {
            return eligibleBranches;
        }

        var visibleBranchIds = offerings
            .Where(offering => offering.BranchId.HasValue)
            .Select(offering => offering.BranchId!.Value)
            .ToHashSet();

        return eligibleBranches
            .Where(branch => visibleBranchIds.Contains(branch.Id))
            .ToArray();
    }

    private static IEnumerable<CatalogOfferingReadModel> GetEligibleOfferings(
        CatalogProviderReadModel provider,
        Guid? categoryId,
        Guid? branchId)
    {
        var eligibleBranchIds = provider.Branches
            .Where(IsEligibleBranch)
            .Select(branch => branch.Id)
            .ToHashSet();

        if (eligibleBranchIds.Count == 0)
        {
            return Array.Empty<CatalogOfferingReadModel>();
        }

        var categories = provider.Categories.AsEnumerable();

        if (categoryId.HasValue)
        {
            categories = categories.Where(category => category.Id == categoryId.Value);
        }

        return categories
            .SelectMany(category => category.Offerings)
            .Where(offering => IsEligibleOffering(offering, eligibleBranchIds))
            .Where(offering => !branchId.HasValue ||
                !offering.BranchId.HasValue ||
                offering.BranchId.Value == branchId.Value);
    }

    private static bool IsEligibleOffering(
        CatalogOfferingReadModel offering,
        IReadOnlySet<Guid> eligibleBranchIds) =>
        !offering.BranchId.HasValue || eligibleBranchIds.Contains(offering.BranchId.Value);

    private static bool IsEligibleBranch(CatalogBranchReadModel branch) =>
        branch.HasPublishedServiceArea && branch.ServiceAreaRadiusKm.HasValue;

    private CatalogBusinessResponse MapBusiness(
        CatalogProviderReadModel provider,
        string language,
        IReadOnlyCollection<CatalogBranchReadModel> visibleBranches)
    {
        return new CatalogBusinessResponse
        {
            Id = provider.Id,
            SourceId = provider.SourceCompanyId,
            Name = SelectLocalizedText(language, provider.NameAr, provider.NameHe),
            Description = SelectLocalizedOptionalText(language, provider.DescriptionAr, provider.DescriptionHe),
            Phone = provider.Phone,
            Catalog = MapMetadata(provider),
            Branches = visibleBranches
                .OrderBy(branch => branch.DisplayOrder)
                .ThenBy(branch => branch.NameAr)
                .Select(branch => MapBranch(language, branch))
                .ToArray()
        };
    }

    private CatalogBusinessContextResponse MapBusinessContext(
        CatalogProviderReadModel provider,
        string language)
    {
        return new CatalogBusinessContextResponse
        {
            Id = provider.Id,
            SourceId = provider.SourceCompanyId,
            Name = SelectLocalizedText(language, provider.NameAr, provider.NameHe),
            Catalog = MapMetadata(provider)
        };
    }

    private CatalogBranchResponse MapBranch(
        string language,
        CatalogBranchReadModel branch)
    {
        return new CatalogBranchResponse
        {
            Id = branch.Id,
            SourceId = branch.SourceBranchId,
            Name = SelectLocalizedText(language, branch.NameAr, branch.NameHe),
            Address = SelectLocalizedText(language, branch.AddressAr, branch.AddressHe),
            Latitude = branch.Latitude,
            Longitude = branch.Longitude,
            ServiceArea = new CatalogServiceAreaResponse
            {
                UsesBranchCoordinates = branch.UsesBranchCoordinates,
                CenterLatitude = branch.ServiceAreaCenterLatitude,
                CenterLongitude = branch.ServiceAreaCenterLongitude,
                RadiusKm = branch.ServiceAreaRadiusKm ?? 0
            }
        };
    }

    private CatalogCategoryResponse MapCategory(
        CatalogProviderReadModel provider,
        CatalogCategoryReadModel category,
        string language)
    {
        return new CatalogCategoryResponse
        {
            Id = category.Id,
            SourceId = category.SourceCategoryId,
            Name = SelectLocalizedText(language, category.NameAr, category.NameHe),
            Description = SelectLocalizedOptionalText(language, category.DescriptionAr, category.DescriptionHe),
            DisplayOrder = category.DisplayOrder,
            Business = MapBusinessContext(provider, language)
        };
    }

    private CatalogOfferingResponse MapOffering(
        CatalogProviderReadModel provider,
        CatalogOfferingReadModel offering,
        string language)
    {
        return new CatalogOfferingResponse
        {
            Id = offering.Id,
            SourceId = offering.SourceOfferingId,
            Name = SelectLocalizedText(language, offering.NameAr, offering.NameHe),
            Description = SelectLocalizedOptionalText(language, offering.DescriptionAr, offering.DescriptionHe),
            BasePrice = offering.BasePrice,
            DurationMinutes = offering.DurationMinutes,
            ImageUrl = offering.ImageUrl,
            ReferenceCode = offering.ReferenceCode,
            DisplayOrder = offering.DisplayOrder,
            Business = MapBusinessContext(provider, language),
            Category = new CatalogCategoryContextResponse
            {
                Id = offering.Category.Id,
                SourceId = offering.Category.SourceCategoryId,
                Name = SelectLocalizedText(language, offering.Category.NameAr, offering.Category.NameHe)
            },
            Branch = offering.Branch is null
                ? null
                : new CatalogBranchContextResponse
                {
                    Id = offering.Branch.Id,
                    SourceId = offering.Branch.SourceBranchId,
                    Name = SelectLocalizedText(language, offering.Branch.NameAr, offering.Branch.NameHe)
                },
            AddonGroups = offering.AddonGroups
                .OrderBy(addonGroup => addonGroup.DisplayOrder)
                .ThenBy(addonGroup => addonGroup.NameAr)
                .Select(addonGroup => new CatalogAddonGroupResponse
                {
                    Id = addonGroup.Id,
                    SourceId = addonGroup.SourceAddonGroupId,
                    Name = SelectLocalizedText(language, addonGroup.NameAr, addonGroup.NameHe),
                    Description = SelectLocalizedOptionalText(
                        language,
                        addonGroup.DescriptionAr,
                        addonGroup.DescriptionHe),
                    SelectionType = addonGroup.SelectionType,
                    IsRequired = addonGroup.IsRequired,
                    MinimumSelections = addonGroup.MinimumSelections,
                    MaximumSelections = addonGroup.MaximumSelections,
                    DisplayOrder = addonGroup.DisplayOrder,
                    Choices = addonGroup.Choices
                        .OrderBy(choice => choice.DisplayOrder)
                        .ThenBy(choice => choice.NameAr)
                        .Select(choice => new CatalogAddonChoiceResponse
                        {
                            Id = choice.Id,
                            SourceId = choice.SourceAddonChoiceId,
                            Name = SelectLocalizedText(language, choice.NameAr, choice.NameHe),
                            Description = SelectLocalizedOptionalText(
                                language,
                                choice.DescriptionAr,
                                choice.DescriptionHe),
                            PriceAdjustment = choice.PriceAdjustment,
                            DurationAdjustmentMinutes = choice.DurationAdjustmentMinutes,
                            DefaultQuantity = choice.DefaultQuantity,
                            DisplayOrder = choice.DisplayOrder
                        })
                        .ToArray()
                })
                .ToArray()
        };
    }

    private CatalogMetadataResponse MapMetadata(CatalogProviderReadModel provider)
    {
        var freshness = CatalogProviderFreshnessState.Create(
            provider,
            _timeProvider.GetUtcNow(),
            _optionsMonitor.CurrentValue);

        return new CatalogMetadataResponse
        {
            Version = provider.CatalogVersion,
            GeneratedAtUtc = provider.SnapshotGeneratedAtUtc,
            RefreshedAtUtc = provider.LastSuccessfulRefreshAtUtc,
            FreshUntilUtc = freshness.FreshUntilUtc,
            MaxStaleUntilUtc = freshness.MaxStaleUntilUtc,
            IsStale = freshness.HasSnapshot && !freshness.IsFresh
        };
    }

    private static string ResolveLanguage(string? requestedLanguage, string? acceptLanguageHeader) =>
        ConfigurationLanguageResolver.Resolve(requestedLanguage, acceptLanguageHeader);

    private static string SelectLocalizedText(
        string language,
        string arabicValue,
        string? hebrewValue)
    {
        return language == ConfigurationLanguageResolver.Hebrew
            ? ConfigurationTextNormalizer.NormalizeOptional(hebrewValue)
                ?? ConfigurationTextNormalizer.NormalizeRequired(arabicValue)
            : ConfigurationTextNormalizer.NormalizeRequired(arabicValue);
    }

    private static string? SelectLocalizedOptionalText(
        string language,
        string? arabicValue,
        string? hebrewValue)
    {
        return language == ConfigurationLanguageResolver.Hebrew
            ? ConfigurationTextNormalizer.NormalizeOptional(hebrewValue)
                ?? ConfigurationTextNormalizer.NormalizeOptional(arabicValue)
            : ConfigurationTextNormalizer.NormalizeOptional(arabicValue);
    }

    private static CatalogReadModelException CreateUnavailable(string message) =>
        new(
            CatalogProblemCodes.Unavailable,
            StatusCodes.Status503ServiceUnavailable,
            message);

    private static CatalogReadModelException CreateBusinessNotFound() =>
        new(
            CatalogProblemCodes.BusinessNotFound,
            StatusCodes.Status404NotFound,
            "The requested catalog business was not found.");

    private static CatalogReadModelException CreateOfferingNotFound() =>
        new(
            CatalogProblemCodes.OfferingNotFound,
            StatusCodes.Status404NotFound,
            "The requested catalog offering was not found.");

    private static CatalogReadModelException CreateFilterMismatch() =>
        new(
            CatalogProblemCodes.FilterMismatch,
            StatusCodes.Status400BadRequest,
            "The supplied catalog filters do not belong to the same business.");

    private sealed record CatalogBusinessProjection(
        CatalogProviderReadModel Provider,
        IReadOnlyCollection<CatalogBranchReadModel> VisibleBranches,
        IReadOnlyCollection<CatalogOfferingReadModel> VisibleOfferings);

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

internal sealed class CatalogSnapshotValidationException : Exception
{
    public CatalogSnapshotValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

internal static class CatalogSnapshotValidator
{
    public static void Validate(Guid expectedCompanyId, CatalogSnapshotResponse snapshot)
    {
        if (!string.Equals(
                snapshot.ContractVersion,
                Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract.Version,
                StringComparison.Ordinal))
        {
            throw new CatalogSnapshotValidationException(
                "catalog_snapshot_contract_invalid",
                "The catalog snapshot contract version is unsupported.");
        }

        if (snapshot.Company.Id != expectedCompanyId)
        {
            throw new CatalogSnapshotValidationException(
                "catalog_snapshot_company_mismatch",
                "The catalog snapshot company does not match the configured provider.");
        }

        EnsureUnique(
            snapshot.Branches.Select(branch => branch.Id),
            "catalog_snapshot_duplicate_branch");
        EnsureUnique(
            snapshot.ServiceAreas.Select(serviceArea => serviceArea.BranchId),
            "catalog_snapshot_duplicate_service_area");
        EnsureUnique(
            snapshot.Categories.Select(category => category.Id),
            "catalog_snapshot_duplicate_category");
        EnsureUnique(
            snapshot.Categories.SelectMany(category => category.Offerings.Select(offering => offering.Id)),
            "catalog_snapshot_duplicate_offering");
        EnsureUnique(
            snapshot.Categories.SelectMany(category =>
                category.Offerings.SelectMany(offering => offering.AddonGroups.Select(addonGroup => addonGroup.Id))),
            "catalog_snapshot_duplicate_addon_group");
        EnsureUnique(
            snapshot.Categories.SelectMany(category =>
                category.Offerings.SelectMany(offering =>
                    offering.AddonGroups.SelectMany(addonGroup => addonGroup.Choices.Select(choice => choice.Id)))),
            "catalog_snapshot_duplicate_addon_choice");

        var branchIds = snapshot.Branches.Select(branch => branch.Id).ToHashSet();
        foreach (var serviceArea in snapshot.ServiceAreas)
        {
            if (!branchIds.Contains(serviceArea.BranchId))
            {
                throw new CatalogSnapshotValidationException(
                    "catalog_snapshot_service_area_branch_missing",
                    "The snapshot contains a service area for an unknown branch.");
            }
        }

        foreach (var category in snapshot.Categories)
        {
            if (string.IsNullOrWhiteSpace(category.NameAr))
            {
                throw new CatalogSnapshotValidationException(
                    "catalog_snapshot_category_name_missing",
                    "A catalog category is missing a required Arabic name.");
            }

            foreach (var offering in category.Offerings)
            {
                if (string.IsNullOrWhiteSpace(offering.NameAr))
                {
                    throw new CatalogSnapshotValidationException(
                        "catalog_snapshot_offering_name_missing",
                        "A catalog offering is missing a required Arabic name.");
                }

                if (offering.BranchId.HasValue && !branchIds.Contains(offering.BranchId.Value))
                {
                    throw new CatalogSnapshotValidationException(
                        "catalog_snapshot_offering_branch_missing",
                        "A catalog offering references an unknown branch.");
                }
            }
        }
    }

    private static void EnsureUnique(
        IEnumerable<Guid> values,
        string code)
    {
        var duplicate = values
            .GroupBy(value => value)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new CatalogSnapshotValidationException(
                code,
                $"The catalog snapshot contains a duplicate source identifier '{duplicate.Key:D}'.");
        }
    }
}

internal static class CatalogSnapshotHasher
{
    private static readonly JsonSerializerOptions JsonOptions =
        Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract.CreateJsonSerializerOptions();

    public static string Compute(CatalogSnapshotResponse snapshot)
    {
        var canonicalSnapshot = new
        {
            snapshot.ContractVersion,
            snapshot.CatalogVersion,
            Company = new
            {
                snapshot.Company.Id,
                snapshot.Company.NameAr,
                snapshot.Company.NameHe,
                snapshot.Company.DescriptionAr,
                snapshot.Company.DescriptionHe,
                snapshot.Company.Phone
            },
            Branches = snapshot.Branches.Select(branch => new
            {
                branch.Id,
                branch.NameAr,
                branch.NameHe,
                branch.AddressAr,
                branch.AddressHe,
                branch.Latitude,
                branch.Longitude
            }).ToArray(),
            ServiceAreas = snapshot.ServiceAreas
                .OrderBy(serviceArea => serviceArea.BranchId)
                .Select(serviceArea => new
                {
                    serviceArea.BranchId,
                    serviceArea.IsActive,
                    serviceArea.UsesBranchCoordinates,
                    serviceArea.CenterLatitude,
                    serviceArea.CenterLongitude,
                    serviceArea.RadiusKm
                }).ToArray(),
            Categories = snapshot.Categories.Select(category => new
            {
                category.Id,
                category.NameAr,
                category.NameHe,
                category.DescriptionAr,
                category.DescriptionHe,
                category.DisplayOrder,
                Offerings = category.Offerings.Select(offering => new
                {
                    offering.Id,
                    offering.BranchId,
                    offering.NameAr,
                    offering.NameHe,
                    offering.DescriptionAr,
                    offering.DescriptionHe,
                    offering.BasePrice,
                    offering.DurationMinutes,
                    offering.ImageUrl,
                    offering.ReferenceCode,
                    offering.DisplayOrder,
                    AddonGroups = offering.AddonGroups.Select(addonGroup => new
                    {
                        addonGroup.Id,
                        addonGroup.NameAr,
                        addonGroup.NameHe,
                        addonGroup.DescriptionAr,
                        addonGroup.DescriptionHe,
                        addonGroup.SelectionType,
                        addonGroup.IsRequired,
                        addonGroup.MinimumSelections,
                        addonGroup.MaximumSelections,
                        addonGroup.DisplayOrder,
                        Choices = addonGroup.Choices.Select(choice => new
                        {
                            choice.Id,
                            choice.NameAr,
                            choice.NameHe,
                            choice.DescriptionAr,
                            choice.DescriptionHe,
                            choice.PriceAdjustment,
                            choice.DurationAdjustmentMinutes,
                            choice.DefaultQuantity,
                            choice.DisplayOrder
                        }).ToArray()
                    }).ToArray()
                }).ToArray()
            }).ToArray()
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(canonicalSnapshot, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}
