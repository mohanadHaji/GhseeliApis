using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using Ghseeli.IntegrationContracts.Vehicles;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Catalog;
using Microsoft.AspNetCore.Http;
using Moq;
using System.Text.Json;

namespace GhseeliApis.Tests.Services.Catalog;

/// <summary>
/// Verifies availability discovery candidate filtering, merge behavior, ordering, and failures.
/// </summary>
public sealed class AvailabilityDiscoveryQueryServiceTests
{
    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-CUSTOMER-002")]
    public async Task SearchAsync_MergesFavouriteRatingsFallbackNamesAndFullOrderingChain()
    {
        var businesses = new[]
        {
            CreateBusiness(1, "שם א", true, 4.8m, 5),
            CreateBusiness(2, "شركة ب", false, 4.8m, 9),
            CreateBusiness(3, "شركة ج", false, 4.8m, 9),
            CreateBusiness(4, "شركة د", false, 4.8m, 9),
            CreateBusiness(5, "شركة هـ", false, 5.0m, 1)
        };
        var providers = businesses.Select((business, index) =>
            CreateProvider(business, index is 2 or 3 ? 5 : index)).ToArray();
        var responseOrder = new[]
        {
            businesses[0], businesses[1], businesses[2], businesses[3], businesses[4]
        };
        var service = CreateService(
            new CatalogBusinessesResponse
            {
                Language = "he",
                Businesses = businesses
            },
            providers,
            request => new AvailabilityDiscoveryResponse
            {
                Date = request.Date,
                PreferredLocalTime = request.PreferredLocalTime,
                GeneratedAtUtc = DateTime.UtcNow,
                Results = responseOrder.Select(business =>
                    CreateResult(
                        business,
                        business == businesses[0]
                            ? new TimeOnly(10, 30)
                            : new TimeOnly(10, 45))).ToArray()
            });

        var response = await service.SearchAsync(
            ValidRequest(),
            "he",
            default);

        response.Language.Should().Be("he");
        response.Results.First().BusinessId.Should().Be(businesses[0].Id);
        response.Results.First().BusinessName.Should().Be("שם א");
        response.Results.First().IsFavourite.Should().BeTrue();
        response.Results.Skip(1).Select(result => result.BusinessId).Should().Equal(
            businesses[4].Id,
            businesses[1].Id,
            businesses[2].Id,
            businesses[3].Id);
        response.Results.ElementAt(1).BusinessName.Should().Be("شركة هـ");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-LOCALIZATION-006")]
    [Trait("ScenarioId", "FAN-AVAILABILITY-LOCALIZATION-033")]
    public async Task SearchAsync_ArabicAndHebrewFallbackPreserveIdenticalSlotFacts()
    {
        var arabicBusiness = CreateBusiness(6, "شركة عربية", false, 0, 0);
        var hebrewFallbackBusiness = CreateBusiness(6, "שם עברי", false, 0, 0);
        arabicBusiness.Branches.Single().Name = "فرع عربي";
        arabicBusiness.Branches.Single().Address = "عنوان عربي";
        hebrewFallbackBusiness.Branches.Single().Name = "فرع عربي";
        hebrewFallbackBusiness.Branches.Single().Address = "عنوان عربي";
        var provider = CreateProvider(arabicBusiness, 0);
        var arabicExpected = CreateResult(arabicBusiness, new TimeOnly(10, 30));
        var hebrewExpected = CreateResult(hebrewFallbackBusiness, new TimeOnly(10, 30));
        foreach (var expected in new[] { arabicExpected, hebrewExpected })
        {
            expected.TimeZoneId = "Asia/Jerusalem";
            expected.SlotStartUtc =
                new DateTime(2026, 9, 28, 7, 30, 0, DateTimeKind.Utc);
            expected.ConfiguredCapacity = 4;
            expected.RemainingCapacity = 3;
        }

        var arabic = await CreateService(
            new CatalogBusinessesResponse
            {
                Language = "ar",
                Businesses = [arabicBusiness]
            },
            [provider],
            request => new AvailabilityDiscoveryResponse
            {
                ContractVersion = BusinessCatalogContract.Version,
                Date = request.Date,
                PreferredLocalTime = request.PreferredLocalTime,
                GeneratedAtUtc = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc),
                Results = [arabicExpected]
            })
            .SearchAsync(ValidRequest(), "ar", default);
        var hebrew = await CreateService(
            new CatalogBusinessesResponse
            {
                Language = "he",
                Businesses = [hebrewFallbackBusiness]
            },
            [provider],
            request => new AvailabilityDiscoveryResponse
            {
                ContractVersion = BusinessCatalogContract.Version,
                Date = request.Date,
                PreferredLocalTime = request.PreferredLocalTime,
                GeneratedAtUtc = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc),
                Results = [hebrewExpected]
            })
            .SearchAsync(ValidRequest(), "he", default);

        arabic.Language.Should().Be("ar");
        hebrew.Language.Should().Be("he");
        arabic.Results.Single().BusinessName.Should().Be("شركة عربية");
        hebrew.Results.Single().BusinessName.Should().Be("שם עברי");
        arabic.Results.Single().BranchName.Should().Be("فرع عربي");
        hebrew.Results.Single().BranchName.Should().Be("فرع عربي");
        arabic.Results.Single().BranchAddress.Should().Be("عنوان عربي");
        hebrew.Results.Single().BranchAddress.Should().Be("عنوان عربي");
        var arabicFacts = arabic.Results.Single();
        var hebrewFacts = hebrew.Results.Single();
        (hebrewFacts.BranchId, hebrewFacts.TimeZoneId, hebrewFacts.SlotStartUtc,
            hebrewFacts.SlotStartLocal, hebrewFacts.ConfiguredCapacity,
            hebrewFacts.RemainingCapacity)
            .Should().Be(
                (arabicFacts.BranchId, arabicFacts.TimeZoneId, arabicFacts.SlotStartUtc,
                    arabicFacts.SlotStartLocal, arabicFacts.ConfiguredCapacity,
                    arabicFacts.RemainingCapacity));
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-FILTER-003")]
    public async Task SearchAsync_SendsOnlyVisibleActiveInAreaCategoryCandidates()
    {
        var eligible = CreateBusiness(10, "مؤهل", false, 0, 0);
        var noAvailability = CreateBusiness(11, "غير نشط", false, 0, 0);
        var outOfArea = CreateBusiness(12, "بعيد", false, 0, 0);
        outOfArea.Branches.Single().ServiceArea = new CatalogServiceAreaResponse
        {
            CenterLatitude = 0,
            CenterLongitude = 0,
            RadiusKm = 1
        };
        var eligibleProvider = CreateProvider(eligible, 0);
        var inactiveProvider = CreateProvider(noAvailability, 1);
        inactiveProvider.Branches.Single().AvailabilitySnapshotJson =
            AvailabilityJson(isActive: false);
        var outOfAreaProvider = CreateProvider(outOfArea, 2);
        var captured = new List<AvailabilityDiscoveryRequest>();
        var service = CreateService(
            new CatalogBusinessesResponse
            {
                Language = "ar",
                Businesses = [eligible, noAvailability, outOfArea]
            },
            [eligibleProvider, inactiveProvider, outOfAreaProvider],
            request =>
            {
                captured.Add(request);
                return new AvailabilityDiscoveryResponse
                {
                    Date = request.Date,
                    PreferredLocalTime = request.PreferredLocalTime,
                    GeneratedAtUtc = DateTime.UtcNow,
                    Results = [CreateResult(eligible, new TimeOnly(10))]
                };
            });
        var search = ValidRequest();
        search.CategoryId = Guid.NewGuid();
        search.Latitude = 31.7683;
        search.Longitude = 35.2137;

        var response = await service.SearchAsync(search, "ar", default);

        response.Results.Should().ContainSingle()
            .Which.BusinessId.Should().Be(eligible.Id);
        captured.Should().ContainSingle();
        captured[0].Candidates.Should().ContainSingle();
        captured[0].Candidates.Single().CompanyId.Should().Be(eligible.SourceId);
        captured[0].Candidates.Single().BranchIds.Should().Equal(
            eligible.Branches.Single().SourceId);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-FILTER-019")]
    public async Task SearchAsync_WhenCategoryHasNoEligibleBusiness_ReturnsEmptyWithoutDiscovery()
    {
        var client = new Mock<IBusinessApiClient>(MockBehavior.Strict);
        var service = CreateService(
            new CatalogBusinessesResponse
            {
                Language = "ar",
                Businesses = []
            },
            [],
            _ => throw new InvalidOperationException(),
            client);
        var request = ValidRequest();
        request.CategoryId = Guid.NewGuid();

        var response = await service.SearchAsync(request, "ar", default);

        response.Results.Should().BeEmpty();
        client.Verify(
            value => value.DiscoverAvailabilityAsync(
                It.IsAny<AvailabilityDiscoveryRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WhenNoEligibleCandidates_DoesNotCallBusinessApi()
    {
        var business = CreateBusiness(20, "غير متاح", false, 0, 0);
        var provider = CreateProvider(business, 0);
        provider.Branches.Single().AvailabilitySnapshotJson = null;
        var client = new Mock<IBusinessApiClient>(MockBehavior.Strict);
        var service = CreateService(
            new CatalogBusinessesResponse
            {
                Language = "ar",
                Businesses = [business]
            },
            [provider],
            _ => throw new InvalidOperationException(),
            client);

        var response = await service.SearchAsync(ValidRequest(), "ar", default);

        response.Results.Should().BeEmpty();
        client.Verify(
            value => value.DiscoverAvailabilityAsync(
                It.IsAny<AvailabilityDiscoveryRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("unavailable", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("timeout", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("http", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("contract", StatusCodes.Status502BadGateway)]
    public async Task SearchAsync_MapsUpstreamFailuresWithoutFabricatingSuccess(
        string failure,
        int expectedStatus)
    {
        var business = CreateBusiness(30, "شركة", false, 0, 0);
        var provider = CreateProvider(business, 0);
        var service = CreateService(
            new CatalogBusinessesResponse
            {
                Language = "ar",
                Businesses = [business]
            },
            [provider],
            _ => throw failure switch
            {
                "unavailable" => new BusinessApiUnavailableException(
                    "unavailable", "corr"),
                "timeout" => new TaskCanceledException("timeout"),
                "http" => new HttpRequestException("network"),
                _ => new BusinessApiContractException("invalid", "corr")
            });

        var action = () => service.SearchAsync(ValidRequest(), "ar", default);

        var exception = await action.Should().ThrowAsync<CatalogReadModelException>();
        exception.Which.Code.Should().Be(CatalogProblemCodes.AvailabilityUnavailable);
        exception.Which.StatusCode.Should().Be(expectedStatus);
    }

    private static AvailabilityDiscoveryQueryService CreateService(
        CatalogBusinessesResponse catalog,
        IReadOnlyList<CatalogProviderReadModel> providers,
        Func<AvailabilityDiscoveryRequest, AvailabilityDiscoveryResponse> discover,
        Mock<IBusinessApiClient>? suppliedClient = null)
    {
        var catalogService = new Mock<ICatalogReadModelService>();
        catalogService.Setup(value => value.GetBusinessesAsync(
                It.IsAny<GetCatalogBusinessesRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        var repository = new Mock<ICatalogReadModelRepository>();
        repository.Setup(value => value.GetEnabledProvidersWithGraphAsync(
                It.IsAny<IReadOnlyCollection<Guid>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providers);
        var client = suppliedClient ?? new Mock<IBusinessApiClient>();
        if (suppliedClient is null)
        {
            client.Setup(value => value.DiscoverAvailabilityAsync(
                    It.IsAny<AvailabilityDiscoveryRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns((AvailabilityDiscoveryRequest request, CancellationToken _) =>
                    Task.FromResult(discover(request)));
        }

        return new AvailabilityDiscoveryQueryService(
            catalogService.Object,
            repository.Object,
            client.Object);
    }

    private static AvailabilitySearchRequest ValidRequest() => new()
    {
        VehicleType = VehicleType.Sedan,
        Date = new DateOnly(2026, 9, 28),
        PreferredLocalTime = new TimeOnly(10, 30)
    };

    private static CatalogBusinessResponse CreateBusiness(
        int id,
        string name,
        bool favourite,
        decimal rating,
        int ratingCount)
    {
        var businessId = Guid.Parse($"00000000-0000-0000-0000-{id:D12}");
        var sourceId = Guid.Parse($"10000000-0000-0000-0000-{id:D12}");
        var branchId = Guid.Parse($"20000000-0000-0000-0000-{id:D12}");
        var sourceBranchId = Guid.Parse($"30000000-0000-0000-0000-{id:D12}");
        return new CatalogBusinessResponse
        {
            Id = businessId,
            SourceId = sourceId,
            Name = name,
            IsFavourite = favourite,
            AverageRating = rating,
            RatingCount = ratingCount,
            Branches =
            [
                new CatalogBranchResponse
                {
                    Id = branchId,
                    SourceId = sourceBranchId,
                    Name = $"فرع {id}",
                    Address = $"عنوان {id}",
                    Latitude = 31.7683,
                    Longitude = 35.2137,
                    ServiceArea = new CatalogServiceAreaResponse
                    {
                        CenterLatitude = 31.7683,
                        CenterLongitude = 35.2137,
                        RadiusKm = 10
                    }
                }
            ]
        };
    }

    private static CatalogProviderReadModel CreateProvider(
        CatalogBusinessResponse business,
        int displayOrder)
    {
        var provider = new CatalogProviderReadModel
        {
            Id = business.Id,
            SourceCompanyId = business.SourceId,
            IsEnabled = true,
            DisplayOrder = displayOrder,
            NameAr = business.Name
        };
        var responseBranch = business.Branches.Single();
        provider.Branches.Add(new CatalogBranchReadModel
        {
            Id = responseBranch.Id,
            Provider = provider,
            ProviderId = provider.Id,
            SourceBranchId = responseBranch.SourceId,
            NameAr = responseBranch.Name,
            AddressAr = responseBranch.Address,
            DisplayOrder = 0,
            AvailabilitySnapshotJson = AvailabilityJson(isActive: true)
        });
        return provider;
    }

    private static string AvailabilityJson(bool isActive) =>
        JsonSerializer.Serialize(
            new CatalogSnapshotBranchAvailability
            {
                IsActive = isActive,
                TimeZoneId = "UTC",
                BookingHorizonDays = 30,
                RecurringSchedules =
                [
                    new CatalogSnapshotRecurringSchedule
                    {
                        DayOfWeek = DayOfWeek.Monday,
                        StartLocalTime = TimeSpan.FromHours(9),
                        EndLocalTime = TimeSpan.FromHours(12),
                        SlotDurationMinutes = 30,
                        Capacity = 2
                    }
                ]
            },
            BusinessCatalogContract.CreateJsonSerializerOptions());

    private static AvailabilityDiscoveryCompanyResult CreateResult(
        CatalogBusinessResponse business,
        TimeOnly time) => new()
    {
        CompanyId = business.SourceId,
        BranchId = business.Branches.Single().SourceId,
        TimeZoneId = "UTC",
        SlotStartLocal = business.Branches.Count == 0
            ? default
            : new DateOnly(2026, 9, 28).ToDateTime(time),
        SlotStartUtc = new DateOnly(2026, 9, 28).ToDateTime(time, DateTimeKind.Utc),
        ConfiguredCapacity = 2,
        RemainingCapacity = 2
    };
}
