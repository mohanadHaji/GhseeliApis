using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Payments;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace GhseeliApis.Tests.Services.Checkout;

/// <summary>
/// Defines authoritative direct and draft repricing behavior.
/// </summary>
public class CheckoutPricingServiceTests
{
    [Fact]
    public async Task RepriceAsync_WhenValidMultipleItems_ComputesTotalsAndDoesNotPersist()
    {
        var snapshot = CreateTwoOfferingSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var firstOffering = snapshot.Categories.Single().Offerings.First();
        var secondOffering = snapshot.Categories.Single().Offerings.Last();
        var firstChoice = firstOffering.AddonGroups.Single().Choices.Single().Id;
        var secondChoice = secondOffering.AddonGroups.Single().Choices.Single().Id;
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        request.Items =
        [
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = firstOffering.Id,
                Selections =
                [
                    new CheckoutDraftSelectionRequest
                    {
                        AddonChoiceSourceId = firstChoice,
                        Quantity = 1
                    }
                ]
            },
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = secondOffering.Id,
                Selections =
                [
                    new CheckoutDraftSelectionRequest
                    {
                        AddonChoiceSourceId = secondChoice,
                        Quantity = 1
                    }
                ]
            }
        ];

        var response = await harness.PricingService.RepriceAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "he",
            CancellationToken.None);

        response.Language.Should().Be("ar");
        response.Intent.Items.Should().HaveCount(2);
        response.Pricing.Currency.Should().Be("ILS");
        response.Pricing.BaseSubtotal.Should().Be(189.00m);
        response.Pricing.AddonSubtotal.Should().Be(21.50m);
        response.Pricing.ItemSubtotal.Should().Be(210.50m);
        response.Pricing.ServiceFee.Should().Be(0m);
        response.Pricing.Tax.Should().Be(0m);
        response.Pricing.GrandTotal.Should().Be(210.50m);
        response.Pricing.TotalDurationMinutes.Should().Be(95);
        response.PaymentCapabilities.Methods.Should().ContainSingle(method =>
            method.Method == "CreditCard" &&
            !method.Enabled &&
            method.ReasonCode == CheckoutPaymentCapabilityReasonCodes.ProviderUnavailable);
        harness.BusinessApiClient.ValidateAppointmentRequests.Should().Be(2);
        harness.BusinessApiClient.ValidationRequests.Select(entry => entry.IdempotencyKey)
            .Should()
            .OnlyHaveUniqueItems();
        (await harness.Context.CheckoutDrafts.CountAsync()).Should().Be(0);
        (await harness.Context.CheckoutDraftPricingSnapshots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RepriceAsync_WhenConfiguredTaxAndFee_RoundsDeterministically()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(
            Guid.NewGuid(),
            version: 7,
            availability: CreateAvailability(slotDurationMinutes: 5));
        await using var harness = await CreateHarnessAsync(
            snapshot,
            pricingOptions: new CheckoutPricingOptions
            {
                Currency = "ILS",
                TaxRatePercent = 17m,
                TaxAppliesToServiceFee = true,
                ServiceFee = new CheckoutServiceFeeOptions
                {
                    Mode = CheckoutServiceFeeMode.Percentage,
                    PercentageRate = 2.5m
                }
            });
        var offering = snapshot.Categories.Single().Offerings.Single();
        var choice = offering.AddonGroups.Single().Choices.Single().Id;
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        request.Items =
        [
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = offering.Id,
                Selections =
                [
                    new CheckoutDraftSelectionRequest
                    {
                        AddonChoiceSourceId = choice,
                        Quantity = 1
                    }
                ]
            }
        ];

        var response = await harness.PricingService.RepriceAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "he",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Pricing.BaseSubtotal.Should().Be(79.50m);
        response.Pricing.AddonSubtotal.Should().Be(9.50m);
        response.Pricing.ItemSubtotal.Should().Be(89.00m);
        response.Pricing.ServiceFee.Should().Be(2.23m);
        response.Pricing.ServiceFeeMode.Should().Be(CheckoutServiceFeeMode.Percentage);
        response.Pricing.TaxableSubtotal.Should().Be(91.23m);
        response.Pricing.Tax.Should().Be(15.51m);
        response.Pricing.GrandTotal.Should().Be(106.74m);
    }

    [Fact]
    public async Task RepriceAsync_WhenBusinessReportsStaleCatalogVersion_RefreshesOnceAndSucceeds()
    {
        var companyId = Guid.NewGuid();
        var initialSnapshot = CatalogTestSupport.CreateSnapshot(companyId, version: 5);
        var refreshedSnapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 6,
            branchId: initialSnapshot.Branches.Single().Id,
            categoryId: initialSnapshot.Categories.Single().Id,
            offeringId: initialSnapshot.Categories.Single().Offerings.Single().Id,
            addonGroupId: initialSnapshot.Categories.Single().Offerings.Single().AddonGroups.Single().Id,
            addonChoiceId: initialSnapshot.Categories.Single().Offerings.Single().AddonGroups.Single().Choices.Single().Id);
        var latestSnapshot = refreshedSnapshot;
        await using var harness = await CreateHarnessAsync(initialSnapshot);
        harness.BusinessApiClient.GetCatalogSnapshotHandler = (_, _) => Task.FromResult(latestSnapshot);
        harness.BusinessApiClient.ValidateAppointmentHandler = (request, _, _) =>
        {
            if (request.ExpectedCatalogVersion == 5)
            {
                return Task.FromResult(CatalogTestSupport.CreateValidationResponse(
                    initialSnapshot,
                    request,
                    catalogVersion: 6,
                    errors:
                    [
                        new AppointmentValidationIssue
                        {
                            Code = AppointmentValidationErrorCodes.StaleCatalogVersion,
                            Field = "expectedCatalogVersion",
                            Message = "The supplied catalog version is stale."
                        }
                    ]));
            }

            return Task.FromResult(CatalogTestSupport.CreateValidationResponse(refreshedSnapshot, request, catalogVersion: 6));
        };

        var response = await harness.PricingService.RepriceAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                initialSnapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Pricing.CatalogVersion.Should().Be(6);
        harness.BusinessApiClient.CatalogSnapshotRequests.Should().Be(1);
        harness.BusinessApiClient.ValidationRequests.Select(entry => entry.Request.ExpectedCatalogVersion)
            .Should()
            .Equal(5, 6);
    }

    [Fact]
    public async Task RepriceAsync_WhenBusinessRemainsStale_RefreshesOnlyOnceAndFailsUnavailable()
    {
        var companyId = Guid.NewGuid();
        var initialSnapshot = CatalogTestSupport.CreateSnapshot(companyId, version: 5);
        var refreshedSnapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 6,
            branchId: initialSnapshot.Branches.Single().Id,
            categoryId: initialSnapshot.Categories.Single().Id,
            offeringId: initialSnapshot.Categories.Single().Offerings.Single().Id,
            addonGroupId: initialSnapshot.Categories.Single().Offerings.Single().AddonGroups.Single().Id,
            addonChoiceId: initialSnapshot.Categories.Single().Offerings.Single().AddonGroups.Single().Choices.Single().Id);
        await using var harness = await CreateHarnessAsync(initialSnapshot);
        harness.BusinessApiClient.GetCatalogSnapshotHandler = (_, _) => Task.FromResult(refreshedSnapshot);
        harness.BusinessApiClient.ValidateAppointmentHandler = (request, _, _) =>
            Task.FromResult(CatalogTestSupport.CreateValidationResponse(
                request.ExpectedCatalogVersion == 5 ? initialSnapshot : refreshedSnapshot,
                request,
                catalogVersion: request.ExpectedCatalogVersion + 1,
                errors:
                [
                    new AppointmentValidationIssue
                    {
                        Code = AppointmentValidationErrorCodes.StaleCatalogVersion,
                        Field = "expectedCatalogVersion",
                        Message = "The supplied catalog version is stale."
                    }
                ]));

        var action = () => harness.PricingService.RepriceAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                initialSnapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutPricingException>()
            .Where(exception =>
                exception.Code == CheckoutPricingProblemCodes.Unavailable &&
                exception.StatusCode == StatusCodes.Status503ServiceUnavailable);
        harness.BusinessApiClient.CatalogSnapshotRequests.Should().Be(1);
        harness.BusinessApiClient.ValidateAppointmentRequests.Should().Be(2);
    }

    [Theory]
    [InlineData("authentication")]
    [InlineData("configuration")]
    [InlineData("timeout")]
    [InlineData("unavailable")]
    [InlineData("conflict")]
    [InlineData("contract")]
    public async Task RepriceAsync_WhenBusinessTransportOrContractFails_MapsStableUnavailableWithoutAdditionalRetries(
        string failureKind)
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 14);
        await using var harness = await CreateHarnessAsync(snapshot);
        harness.BusinessApiClient.ValidateAppointmentHandler = (_, _, _) =>
            Task.FromException<ValidateAppointmentResponse>(CreateBusinessException(failureKind));

        var action = () => harness.PricingService.RepriceAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "he",
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CheckoutPricingException>();
        exception.Which.Code.Should().Be(CheckoutPricingProblemCodes.Unavailable);
        exception.Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        harness.BusinessApiClient.ValidateAppointmentRequests.Should().Be(1);
        harness.BusinessApiClient.CatalogSnapshotRequests.Should().Be(0);
    }

    [Theory]
    [InlineData(AppointmentValidationErrorCodes.OfferingNotFound, CheckoutPricingProblemCodes.SelectionInvalid, 400)]
    [InlineData(AppointmentValidationErrorCodes.SlotUnavailable, CheckoutPricingProblemCodes.SlotUnavailable, 400)]
    [InlineData(AppointmentValidationErrorCodes.OutOfServiceArea, CheckoutPricingProblemCodes.OutOfServiceArea, 400)]
    [InlineData(AppointmentValidationErrorCodes.UnsupportedCurrency, CheckoutPricingProblemCodes.Unavailable, 503)]
    public async Task RepriceAsync_WhenBusinessValidationFails_MapsStableCustomerProblems(
        string businessCode,
        string expectedCode,
        int expectedStatus)
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 4);
        await using var harness = await CreateHarnessAsync(snapshot);
        harness.BusinessApiClient.ValidateAppointmentHandler = (request, _, _) =>
            Task.FromResult(CatalogTestSupport.CreateValidationResponse(
                snapshot,
                request,
                errors:
                [
                    new AppointmentValidationIssue
                    {
                        Code = businessCode,
                        Field = "offeringId",
                        Message = businessCode
                    }
                ]));

        var action = () => harness.PricingService.RepriceAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutPricingException>()
            .Where(exception =>
                exception.Code == expectedCode &&
                exception.StatusCode == expectedStatus);
    }

    [Fact]
    public async Task RepriceAsync_WhenAuthoritativeMoneyExceedsSupportedPrecision_ThrowsStableUnavailable()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 15);
        snapshot.Categories.Single().Offerings.Single().BasePrice = 10_000_000_000_000_000m;
        await using var harness = await CreateHarnessAsync(snapshot);

        var action = () => harness.PricingService.RepriceAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CheckoutPricingException>();
        exception.Which.Code.Should().Be(CheckoutPricingProblemCodes.Unavailable);
        exception.Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        (await harness.Context.CheckoutDraftPricingSnapshots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RepriceDraftAsync_WhenSuccessful_PersistsSnapshotAndClearsRequiresReprice()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 8);
        await using var harness = await CreateHarnessAsync(snapshot);
        var created = await harness.DraftService.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        var response = await harness.PricingService.RepriceDraftAsync(
            created.OrderGuid,
            new RepriceCheckoutDraftRequest
            {
                ExpectedVersion = created.Version
            },
            harness.DeviceId,
            requestedLanguage: "he",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Version.Should().Be(2);
        response.RequiresReprice.Should().BeFalse();
        response.Pricing.Should().NotBeNull();
        response.PaymentCapabilities.Should().NotBeNull();

        var storedDraft = await harness.Context.CheckoutDrafts
            .Include(draft => draft.PricingSnapshot)
                .ThenInclude(snapshotRow => snapshotRow!.Items)
                    .ThenInclude(item => item.Selections)
            .SingleAsync();
        storedDraft.RequiresReprice.Should().BeFalse();
        storedDraft.PublicVersion.Should().Be(2);
        storedDraft.PricingSnapshot.Should().NotBeNull();
        storedDraft.PricingSnapshot!.GrandTotal.Should().Be(response.Pricing!.GrandTotal);
        storedDraft.PricingSnapshot.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task RepriceDraftAsync_WhenDraftExpiresDuringBusinessPricing_ReturnsGoneAndPersistsNothing()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 8);
        await using var harness = await CreateHarnessAsync(snapshot);
        var created = await harness.DraftService.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        harness.BusinessApiClient.ValidateAppointmentHandler = (request, _, _) =>
        {
            harness.TimeProvider.Advance(TimeSpan.FromMinutes(31));
            return Task.FromResult(CatalogTestSupport.CreateValidationResponse(snapshot, request));
        };

        var action = () => harness.PricingService.RepriceDraftAsync(
            created.OrderGuid,
            new RepriceCheckoutDraftRequest
            {
                ExpectedVersion = created.Version
            },
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutPricingException>()
            .Where(exception =>
                exception.Code == CheckoutDraftProblemCodes.Expired &&
                exception.StatusCode == StatusCodes.Status410Gone);

        harness.Context.ChangeTracker.Clear();
        var storedDraft = await harness.Context.CheckoutDrafts
            .Include(draft => draft.PricingSnapshot)
            .SingleAsync();
        storedDraft.PublicVersion.Should().Be(1);
        storedDraft.RequiresReprice.Should().BeTrue();
        storedDraft.PricingSnapshot.Should().BeNull();
        (await harness.Context.CheckoutDraftPricingSnapshots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_AfterSuccessfulReprice_RemovesPersistedPricingAndMarksDraftForReprice()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 8);
        await using var harness = await CreateHarnessAsync(snapshot);
        var created = await harness.DraftService.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        await harness.PricingService.RepriceDraftAsync(
            created.OrderGuid,
            new RepriceCheckoutDraftRequest
            {
                ExpectedVersion = created.Version
            },
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        var updated = await harness.DraftService.UpdateAsync(
            created.OrderGuid,
            CheckoutDraftTestSupport.CreateValidUpdateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
                expectedVersion: 2),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        updated.RequiresReprice.Should().BeTrue();
        updated.Pricing.Should().BeNull();

        var storedDraft = await harness.Context.CheckoutDrafts
            .Include(draft => draft.PricingSnapshot)
            .SingleAsync();
        storedDraft.RequiresReprice.Should().BeTrue();
        storedDraft.PricingSnapshot.Should().BeNull();
    }

    [Fact]
    public async Task RepriceAsync_WhenLahzaConfigured_EnablesCreditCardCapability()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 9);
        await using var harness = await CreateHarnessAsync(
            snapshot,
            lahzaOptions: new LahzaConfigurationOptions
            {
                SecretKey = "sk_test_step11"
            });

        var response = await harness.PricingService.RepriceAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.PaymentCapabilities.Methods.Should().ContainSingle(method =>
            method.Method == "CreditCard" &&
            method.Enabled &&
            method.ReasonCode == null);
    }

    private static async Task<Harness> CreateHarnessAsync(
        CatalogSnapshotResponse snapshot,
        CheckoutPricingOptions? pricingOptions = null,
        LahzaConfigurationOptions? lahzaOptions = null,
        DateTimeOffset? utcNow = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        await CheckoutDraftTestSupport.SeedSnapshotAsync(context, snapshot);

        var pricingLogger = new TestAppLogger();
        var draftLogger = new TestAppLogger();
        var catalogLogger = new TestAppLogger();
        var timeProvider = new ManualTimeProvider(
            utcNow ?? new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var businessApiClient = new ScriptedBusinessApiClient
        {
            GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(snapshot),
            ValidateAppointmentHandler = (request, _, _) =>
                Task.FromResult(CatalogTestSupport.CreateValidationResponse(snapshot, request))
        };
        var catalogOptionsMonitor = new Mock<IOptionsMonitor<CatalogReadModelOptions>>();
        catalogOptionsMonitor.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new CatalogReadModelOptions
            {
                FreshWindowSeconds = 300,
                MaxStaleWindowSeconds = 3600,
                LeaseDurationSeconds = 60
            });
        var lahzaOptionsMonitor = new Mock<IOptionsMonitor<LahzaConfigurationOptions>>();
        lahzaOptionsMonitor.SetupGet(monitor => monitor.CurrentValue)
            .Returns(lahzaOptions ?? new LahzaConfigurationOptions());
        var refreshCoordinator = new CatalogProviderRefreshCoordinator(
            new CatalogReadModelRepository(context),
            businessApiClient,
            catalogOptionsMonitor.Object,
            timeProvider,
            catalogLogger);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[InternalServiceWireConstants.CorrelationIdHeaderName] = "corr-step11-service";
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = httpContext
        };

        return new Harness(
            context,
            new CheckoutDraftService(
                new CheckoutDraftRepository(context),
                new CatalogReadModelRepository(context),
                refreshCoordinator,
                timeProvider,
                Options.Create(new CheckoutDraftOptions
                {
                    LifetimeMinutes = 30
                }),
                draftLogger),
            new CheckoutPricingService(
                new CheckoutDraftRepository(context),
                new CatalogReadModelRepository(context),
                refreshCoordinator,
                businessApiClient,
                new CheckoutPaymentCapabilitiesService(lahzaOptionsMonitor.Object),
                httpContextAccessor,
                timeProvider,
                Options.Create(pricingOptions ?? new CheckoutPricingOptions()),
                pricingLogger),
            timeProvider,
            pricingLogger,
            businessApiClient);
    }

    private static CatalogSnapshotResponse CreateTwoOfferingSnapshot(Guid companyId)
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 11,
            availability: CreateAvailability(slotDurationMinutes: 5));
        var category = snapshot.Categories.Single();
        var firstOffering = category.Offerings.Single();
        firstOffering.AddonGroups.Single().Choices.Single().DurationAdjustmentMinutes = 5;
        var secondOffering = new CatalogSnapshotOffering
        {
            Id = Guid.NewGuid(),
            BranchId = firstOffering.BranchId,
            NameAr = "غسيل عميق",
            NameHe = "שטיפה עמוקה",
            DescriptionAr = "وصف ثانٍ",
            DescriptionHe = "תיאור נוסף",
            BasePrice = 109.5m,
            DurationMinutes = 60,
            ReferenceCode = "DEEP-02",
            DisplayOrder = 4,
            AddonGroups =
            [
                new CatalogSnapshotAddonGroup
                {
                    Id = Guid.NewGuid(),
                    NameAr = "إضافات أخرى",
                    NameHe = "תוספות נוספות",
                    SelectionType = "MultipleChoice",
                    IsRequired = false,
                    MinimumSelections = 0,
                    MaximumSelections = 2,
                    DisplayOrder = 1,
                    Choices =
                    [
                        new CatalogSnapshotAddonChoice
                        {
                            Id = Guid.NewGuid(),
                            NameAr = "تلميع",
                            NameHe = "ליטוש",
                            PriceAdjustment = 12m,
                            DurationAdjustmentMinutes = 0,
                            DefaultQuantity = 0,
                            DisplayOrder = 1
                        }
                    ]
                }
            ]
        };

        snapshot.Categories =
        [
            new CatalogSnapshotCategory
            {
                Id = category.Id,
                NameAr = category.NameAr,
                NameHe = category.NameHe,
                DescriptionAr = category.DescriptionAr,
                DescriptionHe = category.DescriptionHe,
                DisplayOrder = category.DisplayOrder,
                Offerings = [firstOffering, secondOffering]
            }
        ];

        return snapshot;
    }

    private static CatalogSnapshotBranchAvailability CreateAvailability(int slotDurationMinutes)
    {
        return new CatalogSnapshotBranchAvailability
        {
            IsActive = true,
            TimeZoneId = "UTC",
            MinimumLeadMinutes = 30,
            BookingHorizonDays = 30,
            RecurringSchedules =
            [
                CreateSchedule(DayOfWeek.Monday, slotDurationMinutes),
                CreateSchedule(DayOfWeek.Tuesday, slotDurationMinutes),
                CreateSchedule(DayOfWeek.Wednesday, slotDurationMinutes),
                CreateSchedule(DayOfWeek.Thursday, slotDurationMinutes),
                CreateSchedule(DayOfWeek.Friday, slotDurationMinutes),
                CreateSchedule(DayOfWeek.Saturday, slotDurationMinutes),
                CreateSchedule(DayOfWeek.Sunday, slotDurationMinutes)
            ]
        };
    }

    private static CatalogSnapshotRecurringSchedule CreateSchedule(
        DayOfWeek dayOfWeek,
        int slotDurationMinutes) =>
        new()
        {
            DayOfWeek = dayOfWeek,
            StartLocalTime = TimeSpan.FromHours(8),
            EndLocalTime = TimeSpan.FromHours(18),
            SlotDurationMinutes = slotDurationMinutes,
            Capacity = 4
        };

    private static BusinessApiException CreateBusinessException(string failureKind) =>
        failureKind switch
        {
            "authentication" => new BusinessApiAuthenticationException("auth failed", "corr-step11-business"),
            "configuration" => new BusinessApiConfigurationException("config failed", "corr-step11-business"),
            "timeout" => new BusinessApiTimeoutException("timeout", "corr-step11-business"),
            "unavailable" => new BusinessApiUnavailableException("unavailable", "corr-step11-business"),
            "conflict" => new BusinessApiConflictException("conflict", "corr-step11-business"),
            "contract" => new BusinessApiContractException("contract", "corr-step11-business"),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind), failureKind, "Unsupported failure kind.")
        };

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(
            ApplicationDbContext context,
            CheckoutDraftService draftService,
            CheckoutPricingService pricingService,
            ManualTimeProvider timeProvider,
            TestAppLogger logger,
            ScriptedBusinessApiClient businessApiClient)
        {
            Context = context;
            DraftService = draftService;
            PricingService = pricingService;
            TimeProvider = timeProvider;
            Logger = logger;
            BusinessApiClient = businessApiClient;
        }

        public ApplicationDbContext Context { get; }
        public CheckoutDraftService DraftService { get; }
        public CheckoutPricingService PricingService { get; }
        public ManualTimeProvider TimeProvider { get; }
        public TestAppLogger Logger { get; }
        public ScriptedBusinessApiClient BusinessApiClient { get; }
        public Guid DeviceId { get; } = Guid.NewGuid();

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
