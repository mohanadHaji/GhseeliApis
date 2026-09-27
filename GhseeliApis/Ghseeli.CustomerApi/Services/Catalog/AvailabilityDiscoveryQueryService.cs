using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Business;
using System.Text.Json;

namespace GhseeliApis.Services.Catalog;

public interface IAvailabilityDiscoveryQueryService
{
    Task<AvailabilitySearchResponse> SearchAsync(
        AvailabilitySearchRequest request,
        string? acceptLanguage,
        CancellationToken cancellationToken);
}

public sealed class AvailabilityDiscoveryQueryService :
    IAvailabilityDiscoveryQueryService
{
    private const double EarthRadiusKm = 6371.0088d;
    private static readonly JsonSerializerOptions JsonOptions =
        Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract
            .CreateJsonSerializerOptions();

    private readonly ICatalogReadModelService _catalogService;
    private readonly ICatalogReadModelRepository _catalogRepository;
    private readonly IBusinessApiClient _businessApiClient;

    public AvailabilityDiscoveryQueryService(
        ICatalogReadModelService catalogService,
        ICatalogReadModelRepository catalogRepository,
        IBusinessApiClient businessApiClient)
    {
        _catalogService = catalogService;
        _catalogRepository = catalogRepository;
        _businessApiClient = businessApiClient;
    }

    public async Task<AvailabilitySearchResponse> SearchAsync(
        AvailabilitySearchRequest request,
        string? acceptLanguage,
        CancellationToken cancellationToken)
    {
        var catalog = await _catalogService.GetBusinessesAsync(
            new GetCatalogBusinessesRequest
            {
                Language = request.Language,
                CategoryId = request.CategoryId
            },
            acceptLanguage,
            cancellationToken);
        if (catalog.Businesses.Count == 0)
        {
            return Empty(request, catalog.Language);
        }

        var providerIds = catalog.Businesses.Select(business => business.Id).ToArray();
        var providers = await _catalogRepository.GetEnabledProvidersWithGraphAsync(
            providerIds,
            cancellationToken);
        var providersById = providers.ToDictionary(provider => provider.Id);
        var mappedBySourceId = catalog.Businesses.ToDictionary(business => business.SourceId);
        var candidates = new List<AvailabilityDiscoveryCompanyCandidate>();
        foreach (var business in catalog.Businesses)
        {
            if (!providersById.TryGetValue(business.Id, out var provider))
            {
                continue;
            }

            var visibleBranches = business.Branches.ToDictionary(branch => branch.SourceId);
            var branchIds = provider.Branches
                .Where(branch => visibleBranches.ContainsKey(branch.SourceBranchId))
                .Where(HasActiveAvailability)
                .Where(branch => IsWithinRequestedArea(
                    visibleBranches[branch.SourceBranchId],
                    request.Latitude,
                    request.Longitude))
                .OrderBy(branch => branch.DisplayOrder)
                .ThenBy(branch => branch.Id)
                .Select(branch => branch.SourceBranchId)
                .ToArray();
            if (branchIds.Length > 0)
            {
                candidates.Add(new AvailabilityDiscoveryCompanyCandidate
                {
                    CompanyId = provider.SourceCompanyId,
                    BranchIds = branchIds
                });
            }
        }

        if (candidates.Count == 0)
        {
            return Empty(request, catalog.Language);
        }

        AvailabilityDiscoveryResponse upstream;
        var upstreamRequest = new AvailabilityDiscoveryRequest
        {
            Date = request.Date,
            PreferredLocalTime = request.PreferredLocalTime,
            CustomerLocation = request.Latitude.HasValue
                ? new AppointmentCustomerLocationFacts
                {
                    Latitude = request.Latitude.Value,
                    Longitude = request.Longitude!.Value
                }
                : null,
            Candidates = candidates
        };
        try
        {
            upstream = await _businessApiClient.DiscoverAvailabilityAsync(
                upstreamRequest,
                cancellationToken);
        }
        catch (BusinessApiUnavailableException exception)
        {
            throw new CatalogReadModelException(
                CatalogProblemCodes.AvailabilityUnavailable,
                StatusCodes.Status503ServiceUnavailable,
                exception.Message);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CatalogReadModelException(
                CatalogProblemCodes.AvailabilityUnavailable,
                StatusCodes.Status503ServiceUnavailable,
                exception.Message);
        }
        catch (HttpRequestException exception)
        {
            throw new CatalogReadModelException(
                CatalogProblemCodes.AvailabilityUnavailable,
                StatusCodes.Status503ServiceUnavailable,
                exception.Message);
        }
        catch (BusinessApiException exception)
        {
            throw new CatalogReadModelException(
                CatalogProblemCodes.AvailabilityUnavailable,
                StatusCodes.Status502BadGateway,
                exception.Message);
        }

        ValidateResponse(upstream, upstreamRequest);
        var candidateByCompany = candidates.ToDictionary(candidate => candidate.CompanyId);
        var results = upstream.Results
            .Select(result =>
            {
                var business = mappedBySourceId[result.CompanyId];
                var branch = business.Branches.Single(item => item.SourceId == result.BranchId);
                var provider = providersById[business.Id];
                var preferred = request.Date.ToDateTime(
                    request.PreferredLocalTime,
                    DateTimeKind.Unspecified);
                return new
                {
                    Distance = (result.SlotStartLocal - preferred).Duration(),
                    provider.DisplayOrder,
                    Result = new AvailabilitySearchResult
                    {
                        BusinessId = business.Id,
                        BusinessName = business.Name,
                        BranchId = branch.Id,
                        BranchName = branch.Name,
                        BranchAddress = branch.Address,
                        IsFavourite = business.IsFavourite,
                        AverageRating = business.AverageRating,
                        RatingCount = business.RatingCount,
                        TimeZoneId = result.TimeZoneId,
                        SlotStartUtc = result.SlotStartUtc,
                        SlotStartLocal = result.SlotStartLocal,
                        ConfiguredCapacity = result.ConfiguredCapacity,
                        RemainingCapacity = result.RemainingCapacity
                    }
                };
            })
            .OrderBy(value => value.Distance)
            .ThenByDescending(value => value.Result.AverageRating)
            .ThenByDescending(value => value.Result.RatingCount)
            .ThenBy(value => value.DisplayOrder)
            .ThenBy(value => value.Result.BusinessId)
            .Select(value => value.Result)
            .ToArray();

        return new AvailabilitySearchResponse
        {
            Language = catalog.Language,
            IsAdvisory = true,
            VehicleType = request.VehicleType,
            Date = request.Date,
            PreferredLocalTime = request.PreferredLocalTime,
            Results = results
        };
    }

    private static bool HasActiveAvailability(CatalogBranchReadModel branch)
    {
        if (string.IsNullOrWhiteSpace(branch.AvailabilitySnapshotJson))
        {
            return false;
        }

        try
        {
            var availability =
                JsonSerializer.Deserialize<CatalogSnapshotBranchAvailability>(
                    branch.AvailabilitySnapshotJson,
                    JsonOptions);
            return availability is { IsActive: true } &&
                   (availability.RecurringSchedules.Count > 0 ||
                    availability.AvailabilityOverrides.Count > 0);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsWithinRequestedArea(
        CatalogBranchResponse branch,
        double? latitude,
        double? longitude)
    {
        if (!latitude.HasValue)
        {
            return true;
        }

        var centerLatitude = branch.ServiceArea.UsesBranchCoordinates
            ? branch.Latitude
            : branch.ServiceArea.CenterLatitude;
        var centerLongitude = branch.ServiceArea.UsesBranchCoordinates
            ? branch.Longitude
            : branch.ServiceArea.CenterLongitude;
        if (!centerLatitude.HasValue || !centerLongitude.HasValue)
        {
            return false;
        }

        return CalculateDistanceKm(
            centerLatitude.Value,
            centerLongitude.Value,
            latitude.Value,
            longitude!.Value) <= branch.ServiceArea.RadiusKm;
    }

    private static double CalculateDistanceKm(
        double startLatitude,
        double startLongitude,
        double endLatitude,
        double endLongitude)
    {
        var deltaLatitude = ToRadians(endLatitude - startLatitude);
        var deltaLongitude = ToRadians(endLongitude - startLongitude);
        var startLatitudeRadians = ToRadians(startLatitude);
        var endLatitudeRadians = ToRadians(endLatitude);
        var haversine = Math.Pow(Math.Sin(deltaLatitude / 2d), 2d)
            + Math.Cos(startLatitudeRadians)
            * Math.Cos(endLatitudeRadians)
            * Math.Pow(Math.Sin(deltaLongitude / 2d), 2d);
        return EarthRadiusKm * 2d * Math.Asin(Math.Min(1d, Math.Sqrt(haversine)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;

    private static void ValidateResponse(
        AvailabilityDiscoveryResponse response,
        AvailabilityDiscoveryRequest request)
    {
        var candidates = request.Candidates.ToDictionary(candidate => candidate.CompanyId);
        var invalidResult = response.Results.Any(result =>
            !candidates.TryGetValue(result.CompanyId, out var candidate) ||
            !candidate.BranchIds.Contains(result.BranchId) ||
            string.IsNullOrWhiteSpace(result.TimeZoneId) ||
            DateOnly.FromDateTime(result.SlotStartLocal) != request.Date ||
            result.SlotStartUtc == default ||
            result.ConfiguredCapacity <= 0 ||
            result.RemainingCapacity <= 0 ||
            result.RemainingCapacity > result.ConfiguredCapacity);
        if (response.ContractVersion !=
                Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract.Version ||
            response.Date != request.Date ||
            response.PreferredLocalTime != request.PreferredLocalTime ||
            response.GeneratedAtUtc == default ||
            response.Results.Count > request.Candidates.Count ||
            response.Results.Select(result => result.CompanyId).Distinct().Count() !=
                response.Results.Count ||
            invalidResult)
        {
            throw new CatalogReadModelException(
                CatalogProblemCodes.AvailabilityUnavailable,
                StatusCodes.Status502BadGateway,
                "The Business API returned an invalid availability-discovery response.");
        }
    }

    private static AvailabilitySearchResponse Empty(
        AvailabilitySearchRequest request,
        string language) => new()
    {
        Language = language,
        IsAdvisory = true,
        VehicleType = request.VehicleType,
        Date = request.Date,
        PreferredLocalTime = request.PreferredLocalTime,
        Results = Array.Empty<AvailabilitySearchResult>()
    };
}
