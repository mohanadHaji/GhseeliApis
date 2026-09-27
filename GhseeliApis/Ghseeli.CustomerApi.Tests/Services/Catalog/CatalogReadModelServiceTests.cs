using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Tests.Support;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;

namespace GhseeliApis.Tests.Services.Catalog;

/// <summary>
/// Defines customer catalog read-model refresh and browse behavior.
/// </summary>
public class CatalogReadModelServiceTests
{
    [Fact]
    public async Task GetBusinessesAsync_SearchTopAndAuthenticatedProjection_AreComposedInBatches()
    {
        var now = new DateTimeOffset(2026, 8, 21, 18, 0, 0, TimeSpan.Zero);
        var first = CreateProviderGraph(Guid.NewGuid(), Guid.NewGuid(), now, 1);
        first.DisplayOrder = 2;
        first.NameAr = "شركة ألف";
        first.NameHe = "חברת אלף";
        var second = CreateProviderGraph(Guid.NewGuid(), Guid.NewGuid(), now, 1);
        second.DisplayOrder = 1;
        second.NameAr = "شركة باء";
        second.NameHe = "חברת בית";
        var repository = new Mock<ICatalogReadModelRepository>();
        repository.Setup(value => value.SynchronizeConfiguredProvidersAsync(
                It.IsAny<IReadOnlyCollection<CatalogProviderRegistrationOptions>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(value => value.ListEnabledProviderSummariesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        repository.Setup(value => value.GetEnabledProvidersWithGraphAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        var reviews = new Mock<IBusinessReviewRepository>();
        reviews.Setup(value => value.GetAggregatesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, GhseeliApis.DTOs.Reviews.BusinessRatingAggregate>
            {
                [first.SourceCompanyId] = new(first.SourceCompanyId, 4.8m, 4),
                [second.SourceCompanyId] = new(second.SourceCompanyId, 4.8m, 2)
            });
        var userId = Guid.NewGuid();
        var favourites = new Mock<IBusinessFavouriteRepository>();
        favourites.Setup(value => value.GetBusinessSourceIdsAsync(
                userId,
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { first.SourceCompanyId });
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "User")
        ], "Test"));
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        var options = new Mock<IOptionsMonitor<CatalogReadModelOptions>>();
        options.SetupGet(value => value.CurrentValue).Returns(new CatalogReadModelOptions
        {
            FreshWindowSeconds = 300,
            MaxStaleWindowSeconds = 3600,
            LeaseDurationSeconds = 30,
            Providers =
            [
                new() { SourceCompanyId = first.SourceCompanyId, Enabled = true },
                new() { SourceCompanyId = second.SourceCompanyId, Enabled = true }
            ]
        });
        var logger = new Mock<IAppLogger>();
        var client = new ScriptedBusinessApiClient();
        var timeProvider = new ManualTimeProvider(now);
        var coordinator = new CatalogProviderRefreshCoordinator(
            repository.Object, client, options.Object, timeProvider, logger.Object);
        var service = new CatalogReadModelService(
            repository.Object,
            client,
            coordinator,
            options.Object,
            timeProvider,
            logger.Object,
            favouriteRepository: favourites.Object,
            reviewRepository: reviews.Object,
            httpContextAccessor: accessor);

        var response = await service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest { Search = "شركة", Top = 5 },
            "ar",
            CancellationToken.None);

        var businesses = response.Businesses.ToArray();
        businesses.Select(value => value.SourceId)
            .Should().Equal(first.SourceCompanyId, second.SourceCompanyId);
        businesses[0].IsFavourite.Should().BeTrue();
        businesses[0].AverageRating.Should().Be(4.8m);
        businesses[0].RatingCount.Should().Be(4);
        businesses[1].IsFavourite.Should().BeFalse();
        reviews.Verify(value => value.GetAggregatesAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        favourites.Verify(value => value.GetBusinessSourceIdsAsync(
            userId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBusinessesAsync_TopFiveAndTen_UseExactRatingCountDisplayNameAndIdOrder()
    {
        var now = new DateTimeOffset(2026, 9, 24, 18, 0, 0, TimeSpan.Zero);
        var providers = Enumerable.Range(0, 12)
            .Select(index => CreateProviderGraph(
                Guid.Parse($"10000000-0000-0000-0000-{index + 1:D12}"),
                Guid.Parse($"20000000-0000-0000-0000-{index + 1:D12}"),
                now,
                1))
            .ToArray();
        for (var index = 0; index < providers.Length; index++)
        {
            providers[index].DisplayOrder = index < 3 ? index : 10;
            providers[index].NameAr = index switch
            {
                3 or 4 => "اسم متساو",
                _ => $"شركة {index:D2}"
            };
        }

        var repository = new Mock<ICatalogReadModelRepository>();
        repository.Setup(value => value.SynchronizeConfiguredProvidersAsync(
                It.IsAny<IReadOnlyCollection<CatalogProviderRegistrationOptions>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(value => value.ListEnabledProviderSummariesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providers);
        repository.Setup(value => value.GetEnabledProvidersWithGraphAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providers);
        var reviews = new Mock<IBusinessReviewRepository>();
        reviews.Setup(value => value.GetAggregatesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, GhseeliApis.DTOs.Reviews.BusinessRatingAggregate>
            {
                [providers[0].SourceCompanyId] = new(providers[0].SourceCompanyId, 5m, 1),
                [providers[1].SourceCompanyId] = new(providers[1].SourceCompanyId, 4.5m, 10),
                [providers[2].SourceCompanyId] = new(providers[2].SourceCompanyId, 4.5m, 5)
            });
        var favourites = new Mock<IBusinessFavouriteRepository>(MockBehavior.Strict);
        var options = new Mock<IOptionsMonitor<CatalogReadModelOptions>>();
        options.SetupGet(value => value.CurrentValue).Returns(new CatalogReadModelOptions
        {
            FreshWindowSeconds = 300,
            MaxStaleWindowSeconds = 3600,
            LeaseDurationSeconds = 30,
            Providers = providers.Select((provider, index) =>
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = provider.SourceCompanyId,
                    Enabled = true,
                    Order = index
                }).ToList()
        });
        var logger = new Mock<IAppLogger>();
        var client = new ScriptedBusinessApiClient();
        var coordinator = new CatalogProviderRefreshCoordinator(
            repository.Object,
            client,
            options.Object,
            new ManualTimeProvider(now),
            logger.Object);
        var service = new CatalogReadModelService(
            repository.Object,
            client,
            coordinator,
            options.Object,
            new ManualTimeProvider(now),
            logger.Object,
            favouriteRepository: favourites.Object,
            reviewRepository: reviews.Object,
            httpContextAccessor: new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext()
            });

        var topFive = (await service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest { Top = 5 },
            "ar",
            CancellationToken.None)).Businesses.ToArray();
        var topTen = (await service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest { Top = 10 },
            "ar",
            CancellationToken.None)).Businesses.ToArray();

        topFive.Should().HaveCount(5);
        topTen.Should().HaveCount(10);
        topFive.Select(value => value.SourceId).Should().Equal(
            providers[0].SourceCompanyId,
            providers[1].SourceCompanyId,
            providers[2].SourceCompanyId,
            providers[3].SourceCompanyId,
            providers[4].SourceCompanyId);
        topFive.Select(value => (value.AverageRating, value.RatingCount)).Should().Equal(
            (5m, 1),
            (4.5m, 10),
            (4.5m, 5),
            (0m, 0),
            (0m, 0));
        favourites.VerifyNoOtherCalls();
        reviews.Verify(value => value.GetAggregatesAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BUSINESS-TOP-038")]
    public async Task GetBusinessesAsync_SearchFiltersBeforeTopRanking()
    {
        var now = new DateTimeOffset(2026, 9, 24, 18, 0, 0, TimeSpan.Zero);
        var providers = Enumerable.Range(0, 6)
            .Select(index => CreateProviderGraph(
                Guid.NewGuid(), Guid.NewGuid(), now, 1))
            .ToArray();
        providers[0].NameAr = "المزود المطابق";
        for (var index = 1; index < providers.Length; index++)
        {
            providers[index].NameAr = $"مزود مرتفع {index}";
        }

        var repository = CreateProjectionRepository(providers);
        var reviews = new Mock<IBusinessReviewRepository>();
        reviews.Setup(value => value.GetAggregatesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providers.ToDictionary(
                provider => provider.SourceCompanyId,
                provider => new GhseeliApis.DTOs.Reviews.BusinessRatingAggregate(
                    provider.SourceCompanyId,
                    provider == providers[0] ? 1m : 5m,
                    provider == providers[0] ? 1 : 100)));
        var service = CreateProjectionService(
            providers, repository, reviews, Mock.Of<IBusinessFavouriteRepository>());

        var response = await service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest
            {
                Search = "المطابق",
                Top = 5
            },
            "ar",
            CancellationToken.None);

        response.Businesses.Should().ContainSingle();
        response.Businesses.Single().SourceId.Should().Be(providers[0].SourceCompanyId);
    }

    [Theory]
    [InlineData("categories")]
    [InlineData("businesses")]
    [InlineData("business")]
    [InlineData("offerings")]
    [InlineData("offering")]
    [Trait("ScenarioId", "FAN-BUSINESS-BATCH-040")]
    public async Task ProjectionMethods_LoadRatingsAndFavouritesOncePerDistinctProviderSet(
        string operation)
    {
        var now = new DateTimeOffset(2026, 9, 24, 18, 0, 0, TimeSpan.Zero);
        var providers = new[]
        {
            CreateProviderGraph(Guid.NewGuid(), Guid.NewGuid(), now, 1),
            CreateProviderGraph(Guid.NewGuid(), Guid.NewGuid(), now, 1)
        };
        var repository = CreateProjectionRepository(providers);
        var userId = Guid.NewGuid();
        IReadOnlyCollection<Guid>? reviewedIds = null;
        IReadOnlyCollection<Guid>? favouriteIds = null;
        var reviews = new Mock<IBusinessReviewRepository>();
        reviews.Setup(value => value.GetAggregatesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, CancellationToken>(
                (ids, _) => reviewedIds = ids.ToArray())
            .ReturnsAsync(new Dictionary<Guid, GhseeliApis.DTOs.Reviews.BusinessRatingAggregate>());
        var favourites = new Mock<IBusinessFavouriteRepository>();
        favourites.Setup(value => value.GetBusinessSourceIdsAsync(
                userId,
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<Guid>, CancellationToken>(
                (_, ids, _) => favouriteIds = ids.ToArray())
            .ReturnsAsync(new HashSet<Guid>());
        var service = CreateProjectionService(
            providers, repository, reviews, favourites.Object, userId);

        switch (operation)
        {
            case "categories":
                await service.GetCategoriesAsync(
                    new GetCatalogCategoriesRequest(), "ar", CancellationToken.None);
                break;
            case "businesses":
                await service.GetBusinessesAsync(
                    new GetCatalogBusinessesRequest(), "ar", CancellationToken.None);
                break;
            case "business":
                await service.GetBusinessAsync(
                    providers[0].Id,
                    new GetCatalogResourceRequest(),
                    "ar",
                    CancellationToken.None);
                break;
            case "offerings":
                await service.GetBusinessOfferingsAsync(
                    providers[0].Id,
                    new GetCatalogBusinessOfferingsRequest(),
                    "ar",
                    CancellationToken.None);
                break;
            case "offering":
                await service.GetOfferingAsync(
                    providers[0].Categories.Single().Offerings.Single().Id,
                    new GetCatalogResourceRequest(),
                    "ar",
                    CancellationToken.None);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        var expectedIds = operation is "categories" or "businesses"
            ? providers.Select(provider => provider.SourceCompanyId)
            : [providers[0].SourceCompanyId];
        reviewedIds.Should().BeEquivalentTo(expectedIds);
        favouriteIds.Should().BeEquivalentTo(expectedIds);
        reviews.Verify(value => value.GetAggregatesAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        favourites.Verify(value => value.GetBusinessSourceIdsAsync(
            userId,
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void CatalogSnapshotHasher_CategoryPresentationChangesAreDeterministicAtFixedVersion()
    {
        var companyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var branchId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var categoryId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var offeringId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var groupId = Guid.Parse("50000000-0000-0000-0000-000000000005");
        var choiceId = Guid.Parse("60000000-0000-0000-0000-000000000006");

        CatalogSnapshotResponse Snapshot(string? imageUrl, string? colorHex) =>
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 77,
                branchId: branchId,
                categoryId: categoryId,
                offeringId: offeringId,
                addonGroupId: groupId,
                addonChoiceId: choiceId,
                categoryImageUrl: imageUrl,
                categoryColorHex: colorHex);

        var nulls = CatalogSnapshotHasher.Compute(Snapshot(null, null));
        var repeatedNulls = CatalogSnapshotHasher.Compute(Snapshot(null, null));
        var imageOnly = CatalogSnapshotHasher.Compute(
            Snapshot("https://cdn.example.test/categories/image.png", null));
        var colorOnly = CatalogSnapshotHasher.Compute(Snapshot(null, "#123456"));
        var both = CatalogSnapshotHasher.Compute(
            Snapshot("https://cdn.example.test/categories/image.png", "#123456"));

        repeatedNulls.Should().Be(nulls);
        new[] { nulls, imageOnly, colorOnly, both }.Should().OnlyHaveUniqueItems();
        CatalogSnapshotHasher.Compute(Snapshot(null, "#123456")).Should().Be(colorOnly);
        CatalogSnapshotHasher.Compute(
                Snapshot("https://cdn.example.test/categories/image.png", null))
            .Should().Be(imageOnly);
    }

    [Theory]
    [InlineData("http://cdn.example.test/category.png", null)]
    [InlineData("/category.png", null)]
    [InlineData("https://user:password@cdn.example.test/category.png", null)]
    [InlineData("malformed", null)]
    [InlineData(null, "#12345G")]
    [InlineData(null, "#1a73e8")]
    public void CatalogSnapshotValidator_RejectsInvalidCategoryPresentation(
        string? imageUrl,
        string? colorHex)
    {
        var companyId = Guid.NewGuid();
        var snapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            categoryImageUrl: imageUrl,
            categoryColorHex: colorHex);

        var action = () => CatalogSnapshotValidator.Validate(companyId, snapshot);

        action.Should().Throw<CatalogSnapshotValidationException>()
            .Where(exception =>
                exception.Code == "catalog_snapshot_category_presentation_invalid");
    }

    [Fact]
    public void CatalogSnapshotValidator_RejectsOverlongCategoryImage()
    {
        var companyId = Guid.NewGuid();
        var snapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            categoryImageUrl: $"https://cdn.example.test/{new string('x', 480)}.png");

        var action = () => CatalogSnapshotValidator.Validate(companyId, snapshot);

        action.Should().Throw<CatalogSnapshotValidationException>()
            .Where(exception =>
                exception.Code == "catalog_snapshot_category_presentation_invalid");
    }

    [Fact]
    public async Task GetCategoriesAsync_PreservesPresentationMetadataAndLocalizedText()
    {
        var sourceCompanyId = Guid.NewGuid();
        using var harness = CreateHarness(sourceCompanyId);
        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                categoryNameAr: "غسيل خارجي",
                categoryNameHe: "שטיפה חיצונית",
                categoryImageUrl: "https://cdn.example.test/categories/exterior.png",
                categoryColorHex: "#1A73E8"));

        var response = await harness.Service.GetCategoriesAsync(
            new GetCatalogCategoriesRequest { Language = "he" },
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        var category = response.Categories.Single();
        category.Name.Should().Be("שטיפה חיצונית");
        category.ImageUrl.Should().Be("https://cdn.example.test/categories/exterior.png");
        category.ColorHex.Should().Be("#1A73E8");
    }

    [Fact]
    public async Task GetCategoriesAsync_MetadataChangeChangesHashAndRefreshesExistingCategory()
    {
        var sourceCompanyId = Guid.NewGuid();
        var sourceCategoryId = Guid.NewGuid();
        using var harness = CreateHarness(sourceCompanyId);
        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 5,
                categoryId: sourceCategoryId,
                categoryImageUrl: null,
                categoryColorHex: null));

        await harness.Service.GetCategoriesAsync(
            new GetCatalogCategoriesRequest(),
            "ar",
            CancellationToken.None);
        var publicId = (await harness.Context.CatalogCategories.SingleAsync()).Id;
        var originalHash = (await harness.Context.CatalogProviders.SingleAsync()).SnapshotHash;

        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 6,
                categoryId: sourceCategoryId,
                categoryImageUrl: "https://cdn.example.test/categories/exterior.png",
                categoryColorHex: "#1A73E8"));

        var refreshed = await harness.Service.GetCategoriesAsync(
            new GetCatalogCategoriesRequest { Refresh = true },
            "ar",
            CancellationToken.None);

        refreshed.Categories.Single().Id.Should().Be(publicId);
        refreshed.Categories.Single().ColorHex.Should().Be("#1A73E8");
        (await harness.Context.CatalogProviders.SingleAsync()).SnapshotHash.Should().NotBe(originalHash);
    }
    [Fact]
    public async Task GetBusinessesAsync_WhenHebrewIsRequested_FallsBackToArabicForMissingLocalizedValues()
    {
        var sourceCompanyId = Guid.NewGuid();
        using var harness = CreateHarness(sourceCompanyId);
        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 1,
                companyNameHe: null,
                branchNameHe: null,
                offeringNameHe: null));

        var response = await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest
            {
                Language = "he"
            },
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Language.Should().Be("he");
        response.Businesses.Should().ContainSingle();
        response.Businesses.Single().Name.Should().Be("مغسلة المدينة");
        response.Businesses.Single().Branches.Single().Name.Should().Be("الفرع الرئيسي");
        response.Businesses.Single().Catalog.Version.Should().Be(1);
        response.Businesses.Single().Catalog.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task GetBusinessesAsync_WhenSameVersionAndSameHashAreVerified_UpdatesFreshnessWithoutChangingPublicIds()
    {
        var sourceCompanyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var offeringId = Guid.NewGuid();
        var addonGroupId = Guid.NewGuid();
        var addonChoiceId = Guid.NewGuid();
        using var harness = CreateHarness(sourceCompanyId);
        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 4,
                branchId: branchId,
                categoryId: categoryId,
                offeringId: offeringId,
                addonGroupId: addonGroupId,
                addonChoiceId: addonChoiceId));

        var firstResponse = await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        var firstBusinessId = firstResponse.Businesses.Single().Id;
        var firstRefreshTime = firstResponse.Businesses.Single().Catalog.RefreshedAtUtc;
        var firstOfferingId = await harness.Context.CatalogOfferings
            .Select(offering => offering.Id)
            .SingleAsync();

        harness.TimeProvider.Advance(TimeSpan.FromMinutes(30));
        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 4,
                branchId: branchId,
                categoryId: categoryId,
                offeringId: offeringId,
                addonGroupId: addonGroupId,
                addonChoiceId: addonChoiceId));

        var secondResponse = await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest
            {
                Refresh = true
            },
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        secondResponse.Businesses.Single().Id.Should().Be(firstBusinessId);
        secondResponse.Businesses.Single().Catalog.RefreshedAtUtc.Should().BeAfter(firstRefreshTime!.Value);
        (await harness.Context.CatalogOfferings.Select(offering => offering.Id).SingleAsync())
            .Should()
            .Be(firstOfferingId);
    }

    [Fact]
    public async Task GetBusinessesAsync_WhenSameVersionHasDifferentHash_ReappliesSnapshotWithoutChangingIds()
    {
        var sourceCompanyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var offeringId = Guid.NewGuid();
        var addonGroupId = Guid.NewGuid();
        var addonChoiceId = Guid.NewGuid();
        using var harness = CreateHarness(sourceCompanyId);
        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 7,
                branchId: branchId,
                categoryId: categoryId,
                offeringId: offeringId,
                addonGroupId: addonGroupId,
                addonChoiceId: addonChoiceId,
                companyNameAr: "الأصلية",
                offeringNameAr: "الخدمة الأصلية"));

        var firstResponse = await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        var firstBusinessId = firstResponse.Businesses.Single().Id;
        var firstOfferingId = await harness.Context.CatalogOfferings
            .Select(offering => offering.Id)
            .SingleAsync();

        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 7,
                branchId: branchId,
                categoryId: categoryId,
                offeringId: offeringId,
                addonGroupId: addonGroupId,
                addonChoiceId: addonChoiceId,
                companyNameAr: "المحدّثة",
                offeringNameAr: "الخدمة المحدّثة"));

        var secondResponse = await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest
            {
                Refresh = true
            },
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        secondResponse.Businesses.Single().Id.Should().Be(firstBusinessId);
        secondResponse.Businesses.Single().Name.Should().Be("المحدّثة");
        var offering = await harness.Context.CatalogOfferings.SingleAsync();
        offering.Id.Should().Be(firstOfferingId);
        offering.NameAr.Should().Be("الخدمة المحدّثة");
    }

    [Fact]
    public async Task GetBusinessOfferingsAsync_MetadataOnlySameVersionChange_ReappliesClearsBadgeAndUsesHebrewFallback()
    {
        var sourceCompanyId = Guid.NewGuid();
        var offeringSourceId = Guid.NewGuid();
        using var harness = CreateHarness(sourceCompanyId);
        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 7,
                offeringId: offeringSourceId,
                offeringQualifierAr: "بدون التعقيم",
                offeringQualifierHe: "ללא חיטוי",
                offeringBadgeCode: CatalogOfferingBadgeCode.MostRequested));

        var businesses = await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        var businessId = businesses.Businesses.Single().Id;
        var arabic = await harness.Service.GetBusinessOfferingsAsync(
            businessId,
            new GetCatalogBusinessOfferingsRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        arabic.Offerings.Single().Qualifier.Should().Be("بدون التعقيم");
        arabic.Offerings.Single().BadgeCode.Should().Be(CatalogOfferingBadgeCode.MostRequested);

        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 7,
                offeringId: offeringSourceId,
                offeringQualifierAr: "بدون تلميع",
                offeringQualifierHe: null,
                offeringBadgeCode: null));

        var hebrew = await harness.Service.GetBusinessOfferingsAsync(
            businessId,
            new GetCatalogBusinessOfferingsRequest { Refresh = true, Language = "he" },
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        var persisted = await harness.Context.CatalogOfferings.SingleAsync();

        hebrew.Offerings.Single().Qualifier.Should().Be("بدون تلميع");
        hebrew.Offerings.Single().BadgeCode.Should().BeNull();
        persisted.QualifierAr.Should().Be("بدون تلميع");
        persisted.QualifierHe.Should().BeNull();
        persisted.BadgeCode.Should().BeNull();
    }

    [Fact]
    public async Task GetBusinessesAsync_WhenLowerVersionIsReturned_KeepsExistingSnapshot()
    {
        var sourceCompanyId = Guid.NewGuid();
        using var harness = CreateHarness(sourceCompanyId);
        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 5,
                companyNameAr: "الإصدار الحديث"));

        await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 4,
                companyNameAr: "الإصدار القديم"));

        var response = await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest
            {
                Refresh = true
            },
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Businesses.Single().Name.Should().Be("الإصدار الحديث");
        response.Businesses.Single().Catalog.Version.Should().Be(5);
    }

    [Fact]
    public async Task GetBusinessesAsync_WhenNoProvidersAreConfigured_ReturnsIntentionalEmptyResponse()
    {
        using var harness = CreateHarness(300, 3600, 30);

        var response = await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Language.Should().Be("ar");
        response.Businesses.Should().BeEmpty();
        harness.Client.CatalogSnapshotRequests.Should().Be(0);
    }

    [Fact]
    public async Task GetCategoriesAsync_WhenNoProvidersAreConfigured_ReturnsIntentionalEmptyResponse()
    {
        using var harness = CreateHarness(300, 3600, 30);

        var response = await harness.Service.GetCategoriesAsync(
            new GetCatalogCategoriesRequest(),
            acceptLanguageHeader: "he",
            CancellationToken.None);

        response.Language.Should().Be("he");
        response.Categories.Should().BeEmpty();
        harness.Client.CatalogSnapshotRequests.Should().Be(0);
    }

    [Fact]
    public async Task GetBusinessesAsync_WhenRefreshFailsButCacheIsWithinMaxStale_ServesStaleData()
    {
        var sourceCompanyId = Guid.NewGuid();
        using var harness = CreateHarness(
            sourceCompanyId,
            freshWindowSeconds: 60,
            maxStaleWindowSeconds: 600);
        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(companyId, version: 2));

        await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        harness.TimeProvider.Advance(TimeSpan.FromMinutes(5));
        harness.Client.GetCatalogSnapshotHandler = (_, _) =>
            throw new BusinessApiUnavailableException("Downstream unavailable.", "corr-step9-stale");

        var response = await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest
            {
                Refresh = true
            },
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Businesses.Single().Catalog.Version.Should().Be(2);
        response.Businesses.Single().Catalog.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task GetBusinessesAsync_WhenRefreshFailsAndCacheIsBeyondMaxStale_ThrowsUnavailable()
    {
        var sourceCompanyId = Guid.NewGuid();
        using var harness = CreateHarness(
            sourceCompanyId,
            freshWindowSeconds: 60,
            maxStaleWindowSeconds: 120);
        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            CatalogTestSupport.CreateSnapshot(companyId, version: 2));

        await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        harness.TimeProvider.Advance(TimeSpan.FromMinutes(5));
        harness.Client.GetCatalogSnapshotHandler = (_, _) =>
            throw new BusinessApiUnavailableException("Downstream unavailable.", "corr-step9-hard-stale");

        var action = () => harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CatalogReadModelException>()
            .Where(exception =>
                exception.Code == CatalogProblemCodes.Unavailable &&
                exception.StatusCode == StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task GetBusinessesAsync_WhenLeaseIsLostButUsableStaleCacheExists_ServesStaleData()
    {
        var providerId = Guid.NewGuid();
        var sourceCompanyId = Guid.NewGuid();
        var staleRefreshAt = new DateTimeOffset(2026, 8, 21, 17, 56, 0, TimeSpan.Zero);
        var providerSummary = CreateProviderSummary(providerId, sourceCompanyId, staleRefreshAt, version: 4);
        var providerGraph = CreateProviderGraph(providerId, sourceCompanyId, staleRefreshAt, version: 4);
        var repository = new Mock<ICatalogReadModelRepository>(MockBehavior.Strict);
        repository.Setup(repo => repo.SynchronizeConfiguredProvidersAsync(
                It.IsAny<IReadOnlyCollection<CatalogProviderRegistrationOptions>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(repo => repo.ListEnabledProviderSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([providerSummary]);
        repository.Setup(repo => repo.TryAcquireRefreshLeaseAsync(
                providerId,
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository.Setup(repo => repo.ApplySnapshotAsync(
                providerId,
                It.IsAny<Ghseeli.IntegrationContracts.BusinessCatalog.CatalogSnapshotResponse>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CatalogSnapshotApplyResult.LeaseLost);
        repository.Setup(repo => repo.GetEnabledProviderSummaryAsync(
                providerId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerSummary);
        repository.Setup(repo => repo.GetEnabledProvidersWithGraphAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Single() == providerId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([providerGraph]);

        var service = CreateService(
            repository.Object,
            timeProvider: new ManualTimeProvider(new DateTimeOffset(2026, 8, 21, 18, 0, 0, TimeSpan.Zero)),
            new CatalogProviderRegistrationOptions
            {
                SourceCompanyId = sourceCompanyId,
                Enabled = true,
                Order = 0
            });
        var client = (ScriptedBusinessApiClient)service.Client;

        var response = await service.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest
            {
                Refresh = true
            },
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Businesses.Should().ContainSingle();
        response.Businesses.Single().Catalog.Version.Should().Be(4);
        response.Businesses.Single().Catalog.IsStale.Should().BeTrue();
        client.CatalogSnapshotRequests.Should().Be(1);
    }

    [Fact]
    public async Task GetBusinessesAsync_WhenLeaseIsLostAndNoUsableCacheExists_ThrowsUnavailable()
    {
        var providerId = Guid.NewGuid();
        var sourceCompanyId = Guid.NewGuid();
        var repository = new Mock<ICatalogReadModelRepository>(MockBehavior.Strict);
        repository.Setup(repo => repo.SynchronizeConfiguredProvidersAsync(
                It.IsAny<IReadOnlyCollection<CatalogProviderRegistrationOptions>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(repo => repo.ListEnabledProviderSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new CatalogProviderReadModel
                {
                    Id = providerId,
                    SourceCompanyId = sourceCompanyId,
                    IsEnabled = true,
                    DisplayOrder = 0
                }
            ]);
        repository.Setup(repo => repo.TryAcquireRefreshLeaseAsync(
                providerId,
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository.Setup(repo => repo.ApplySnapshotAsync(
                providerId,
                It.IsAny<Ghseeli.IntegrationContracts.BusinessCatalog.CatalogSnapshotResponse>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CatalogSnapshotApplyResult.LeaseLost);
        repository.Setup(repo => repo.GetEnabledProviderSummaryAsync(
                providerId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogProviderReadModel?)null);

        var service = CreateService(
            repository.Object,
            timeProvider: new ManualTimeProvider(new DateTimeOffset(2026, 8, 21, 18, 0, 0, TimeSpan.Zero)),
            new CatalogProviderRegistrationOptions
            {
                SourceCompanyId = sourceCompanyId,
                Enabled = true,
                Order = 0
            });

        var action = () => service.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CatalogReadModelException>()
            .Where(exception =>
                exception.Code == CatalogProblemCodes.Unavailable &&
                exception.StatusCode == StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task GetBusinessOfferingsAsync_WhenBranchAndCategoryBelongToDifferentProviders_ThrowsMismatch()
    {
        var firstSourceCompanyId = Guid.NewGuid();
        var secondSourceCompanyId = Guid.NewGuid();
        using var harness = CreateHarness(
            firstSourceCompanyId,
            secondSourceCompanyId);
        harness.Client.GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(
            companyId == firstSourceCompanyId
                ? CatalogTestSupport.CreateSnapshot(
                    companyId,
                    version: 1,
                    companyNameAr: "الأول")
                : CatalogTestSupport.CreateSnapshot(
                    companyId,
                    version: 1,
                    companyNameAr: "الثاني"));

        var businesses = await harness.Service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        var firstBusinessId = businesses.Businesses.Single(business => business.Name == "الأول").Id;
        var firstCategoryId = await harness.Context.CatalogCategories
            .Where(category => category.Provider.NameAr == "الأول")
            .Select(category => category.Id)
            .SingleAsync();
        var foreignBranchId = await harness.Context.CatalogBranches
            .Where(branch => branch.Provider.NameAr == "الثاني")
            .Select(branch => branch.Id)
            .SingleAsync();

        var action = () => harness.Service.GetBusinessOfferingsAsync(
            firstBusinessId,
            new GetCatalogBusinessOfferingsRequest
            {
                CategoryId = firstCategoryId,
                BranchId = foreignBranchId
            },
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CatalogReadModelException>()
            .Where(exception =>
                exception.Code == CatalogProblemCodes.FilterMismatch &&
                exception.StatusCode == StatusCodes.Status400BadRequest);
    }

    private static Mock<ICatalogReadModelRepository> CreateProjectionRepository(
        IReadOnlyList<CatalogProviderReadModel> providers)
    {
        var repository = new Mock<ICatalogReadModelRepository>();
        repository.Setup(value => value.SynchronizeConfiguredProvidersAsync(
                It.IsAny<IReadOnlyCollection<CatalogProviderRegistrationOptions>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(value => value.ListEnabledProviderSummariesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(providers);
        repository.Setup(value => value.GetEnabledProvidersWithGraphAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                providers.Where(provider => ids.Contains(provider.Id)).ToArray());
        foreach (var provider in providers)
        {
            repository.Setup(value => value.GetEnabledProviderSummaryAsync(
                    provider.Id,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(provider);
            foreach (var offering in provider.Categories.SelectMany(value => value.Offerings))
            {
                repository.Setup(value => value.GetEnabledOfferingSummaryAsync(
                        offering.Id,
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(offering);
            }
        }

        return repository;
    }

    private static CatalogReadModelService CreateProjectionService(
        IReadOnlyList<CatalogProviderReadModel> providers,
        Mock<ICatalogReadModelRepository> repository,
        Mock<IBusinessReviewRepository> reviews,
        IBusinessFavouriteRepository favourites,
        Guid? userId = null)
    {
        var options = new Mock<IOptionsMonitor<CatalogReadModelOptions>>();
        options.SetupGet(value => value.CurrentValue).Returns(new CatalogReadModelOptions
        {
            FreshWindowSeconds = 300,
            MaxStaleWindowSeconds = 3600,
            LeaseDurationSeconds = 30,
            Providers = providers.Select((provider, index) =>
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = provider.SourceCompanyId,
                    Enabled = true,
                    Order = index
                }).ToList()
        });
        var client = new ScriptedBusinessApiClient();
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 24, 18, 0, 0, TimeSpan.Zero));
        var logger = new Mock<IAppLogger>();
        var coordinator = new CatalogProviderRefreshCoordinator(
            repository.Object,
            client,
            options.Object,
            timeProvider,
            logger.Object);
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = userId.HasValue
                    ? new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()),
                        new Claim(ClaimTypes.Role, "User")
                    ], "Test"))
                    : new ClaimsPrincipal(new ClaimsIdentity())
            }
        };
        return new CatalogReadModelService(
            repository.Object,
            client,
            coordinator,
            options.Object,
            timeProvider,
            logger.Object,
            favouriteRepository: favourites,
            reviewRepository: reviews.Object,
            httpContextAccessor: accessor);
    }

    private static CatalogServiceHarness CreateHarness(
        Guid sourceCompanyId,
        double freshWindowSeconds = 300,
        double maxStaleWindowSeconds = 3600,
        double leaseDurationSeconds = 30) =>
        CreateHarness(
            freshWindowSeconds,
            maxStaleWindowSeconds,
            leaseDurationSeconds,
            new CatalogProviderRegistrationOptions
            {
                SourceCompanyId = sourceCompanyId,
                Enabled = true,
                Order = 0
            });

    private static CatalogServiceHarness CreateHarness(
        Guid firstSourceCompanyId,
        Guid secondSourceCompanyId) =>
        CreateHarness(
            300,
            3600,
            30,
            new CatalogProviderRegistrationOptions
            {
                SourceCompanyId = firstSourceCompanyId,
                Enabled = true,
                Order = 0
            },
            new CatalogProviderRegistrationOptions
            {
                SourceCompanyId = secondSourceCompanyId,
                Enabled = true,
                Order = 1
            });

    private static CatalogServiceHarness CreateHarness(
        double freshWindowSeconds,
        double maxStaleWindowSeconds,
        double leaseDurationSeconds,
        params CatalogProviderRegistrationOptions[] providers)
    {
        var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        context.Database.EnsureCreated();
        var repository = new CatalogReadModelRepository(context);
        var client = new ScriptedBusinessApiClient();
        var options = new CatalogReadModelOptions
        {
            FreshWindowSeconds = freshWindowSeconds,
            MaxStaleWindowSeconds = maxStaleWindowSeconds,
            LeaseDurationSeconds = leaseDurationSeconds,
            Providers = providers.ToList()
        };
        var optionsMonitor = new Mock<IOptionsMonitor<CatalogReadModelOptions>>();
        optionsMonitor.SetupGet(monitor => monitor.CurrentValue)
            .Returns(options);
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 21, 18, 0, 0, TimeSpan.Zero));
        var logger = new Mock<IAppLogger>();
        var refreshCoordinator = new CatalogProviderRefreshCoordinator(
            repository,
            client,
            optionsMonitor.Object,
            timeProvider,
            logger.Object);

        var service = new CatalogReadModelService(
            repository,
            client,
            refreshCoordinator,
            optionsMonitor.Object,
            timeProvider,
            logger.Object);

        return new CatalogServiceHarness(context, service, client, timeProvider);
    }

    private static MockedCatalogServiceHarness CreateService(
        ICatalogReadModelRepository repository,
        ManualTimeProvider timeProvider,
        params CatalogProviderRegistrationOptions[] providers)
    {
        var client = new ScriptedBusinessApiClient
        {
            GetCatalogSnapshotHandler = (companyId, _) =>
                Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId, version: 5))
        };
        var optionsMonitor = new Mock<IOptionsMonitor<CatalogReadModelOptions>>();
        optionsMonitor.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new CatalogReadModelOptions
            {
                FreshWindowSeconds = 60,
                MaxStaleWindowSeconds = 600,
                LeaseDurationSeconds = 15,
                Providers = providers.ToList()
            });
        var logger = new Mock<IAppLogger>();
        var refreshCoordinator = new CatalogProviderRefreshCoordinator(
            repository,
            client,
            optionsMonitor.Object,
            timeProvider,
            logger.Object);

        return new MockedCatalogServiceHarness(
            new CatalogReadModelService(
                repository,
                client,
                refreshCoordinator,
                optionsMonitor.Object,
                timeProvider,
                logger.Object),
            client);
    }

    private static CatalogProviderReadModel CreateProviderSummary(
        Guid providerId,
        Guid sourceCompanyId,
        DateTimeOffset lastSuccessfulRefreshAtUtc,
        long version)
    {
        return new CatalogProviderReadModel
        {
            Id = providerId,
            SourceCompanyId = sourceCompanyId,
            IsEnabled = true,
            DisplayOrder = 0,
            NameAr = "مغسلة المدينة",
            CatalogVersion = version,
            SnapshotHash = $"hash-{version}",
            LastSuccessfulRefreshAtUtc = lastSuccessfulRefreshAtUtc,
            SnapshotGeneratedAtUtc = lastSuccessfulRefreshAtUtc.AddMinutes(-1)
        };
    }

    private static CatalogProviderReadModel CreateProviderGraph(
        Guid providerId,
        Guid sourceCompanyId,
        DateTimeOffset lastSuccessfulRefreshAtUtc,
        long version)
    {
        var provider = CreateProviderSummary(
            providerId,
            sourceCompanyId,
            lastSuccessfulRefreshAtUtc,
            version);
        var branch = new CatalogBranchReadModel
        {
            Id = Guid.NewGuid(),
            SourceBranchId = Guid.NewGuid(),
            Provider = provider,
            ProviderId = providerId,
            NameAr = "الفرع الرئيسي",
            AddressAr = "العنوان",
            HasPublishedServiceArea = true,
            UsesBranchCoordinates = true,
            ServiceAreaCenterLatitude = 32.1,
            ServiceAreaCenterLongitude = 34.8,
            ServiceAreaRadiusKm = 12,
            Latitude = 32.1,
            Longitude = 34.8
        };
        var category = new CatalogCategoryReadModel
        {
            Id = Guid.NewGuid(),
            SourceCategoryId = Guid.NewGuid(),
            Provider = provider,
            ProviderId = providerId,
            NameAr = "غسيل خارجي",
            DisplayOrder = 0
        };
        var offering = new CatalogOfferingReadModel
        {
            Id = Guid.NewGuid(),
            SourceOfferingId = Guid.NewGuid(),
            Category = category,
            CategoryId = category.Id,
            Branch = branch,
            BranchId = branch.Id,
            NameAr = "غسيل سريع",
            BasePrice = 80,
            DurationMinutes = 30,
            DisplayOrder = 0
        };

        provider.Branches.Add(branch);
        provider.Categories.Add(category);
        category.Offerings.Add(offering);

        return provider;
    }

    private sealed class CatalogServiceHarness : IDisposable
    {
        public CatalogServiceHarness(
            ApplicationDbContext context,
            CatalogReadModelService service,
            ScriptedBusinessApiClient client,
            ManualTimeProvider timeProvider)
        {
            Context = context;
            Service = service;
            Client = client;
            TimeProvider = timeProvider;
        }

        public ApplicationDbContext Context { get; }
        public CatalogReadModelService Service { get; }
        public ScriptedBusinessApiClient Client { get; }
        public ManualTimeProvider TimeProvider { get; }

        public void Dispose()
        {
            Context.Dispose();
        }
    }

    private sealed record MockedCatalogServiceHarness(
        CatalogReadModelService Service,
        ScriptedBusinessApiClient Client);
}
