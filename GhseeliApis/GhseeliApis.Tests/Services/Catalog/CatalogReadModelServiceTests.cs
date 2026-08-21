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
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace GhseeliApis.Tests.Services.Catalog;

/// <summary>
/// Defines customer catalog read-model refresh and browse behavior.
/// </summary>
public class CatalogReadModelServiceTests
{
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

        var service = new CatalogReadModelService(
            repository,
            client,
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

        return new MockedCatalogServiceHarness(
            new CatalogReadModelService(
                repository,
                client,
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
