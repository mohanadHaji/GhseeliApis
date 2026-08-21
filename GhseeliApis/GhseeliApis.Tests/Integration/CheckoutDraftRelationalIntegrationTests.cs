using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Verifies SQL Server rowversion behavior for checkout draft updates.
/// </summary>
public class CheckoutDraftRelationalIntegrationTests
{
    [Fact]
    public async Task CheckoutDrafts_WhenConcurrentWritersSaveSameRow_SecondWriteConflictsAndVersionIncrementsOnce()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 11);
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var deviceId = Guid.NewGuid();

        await using (var seedContext = database.CreateContext())
        {
            await CheckoutDraftTestSupport.SeedSnapshotAsync(seedContext, snapshot);
            var service = CreateService(seedContext, timeProvider);
            await service.CreateAsync(
                CheckoutDraftTestSupport.CreateValidCreateRequest(
                    snapshot,
                    new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
                deviceId,
                requestedLanguage: "ar",
                acceptLanguageHeader: "ar",
                CancellationToken.None);
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();

        var firstDraft = await firstContext.CheckoutDrafts.SingleAsync();
        var secondDraft = await secondContext.CheckoutDrafts.SingleAsync();
        var originalVersion = firstDraft.PublicVersion;

        firstDraft.VehicleColor = "Blue";
        firstDraft.PublicVersion = checked(firstDraft.PublicVersion + 1);
        secondDraft.VehicleColor = "Green";
        secondDraft.PublicVersion = checked(secondDraft.PublicVersion + 1);

        await firstContext.SaveChangesAsync();

        Func<Task> action = async () => await secondContext.SaveChangesAsync();

        await action.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verificationContext = database.CreateContext();
        var storedDraft = await verificationContext.CheckoutDrafts.SingleAsync();
        storedDraft.PublicVersion.Should().Be(originalVersion + 1);
        storedDraft.VehicleColor.Should().Be("Blue");
    }

    [Fact]
    public async Task CheckoutDraftItems_WhenDuplicateOfferingIsInsertedForSameDraft_SaveFailsAtDatabaseBoundary()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 12);
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var deviceId = Guid.NewGuid();

        await using (var seedContext = database.CreateContext())
        {
            await CheckoutDraftTestSupport.SeedSnapshotAsync(seedContext, snapshot);
            var service = CreateService(seedContext, timeProvider);
            await service.CreateAsync(
                CheckoutDraftTestSupport.CreateValidCreateRequest(
                    snapshot,
                    new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
                deviceId,
                requestedLanguage: "ar",
                acceptLanguageHeader: "ar",
                CancellationToken.None);
        }

        await using var context = database.CreateContext();
        var storedDraft = await context.CheckoutDrafts
            .Include(draft => draft.Items)
            .SingleAsync();
        var originalItem = storedDraft.Items.Single();

        context.CheckoutDraftItems.Add(new CheckoutDraftItem
        {
            Id = Guid.NewGuid(),
            CheckoutDraftId = storedDraft.Id,
            OfferingSourceId = originalItem.OfferingSourceId,
            DisplayOrder = originalItem.DisplayOrder + 1
        });

        var action = () => context.SaveChangesAsync();

        await action.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CheckoutDraftSelections_WhenDuplicateAddonChoiceIsInsertedForSameItem_SaveFailsAtDatabaseBoundary()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 13);
        snapshot.Categories.Single().Offerings.Single().AddonGroups.Single().Choices.Single().DurationAdjustmentMinutes = 0;
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var deviceId = Guid.NewGuid();
        var choiceId = snapshot.Categories.Single().Offerings.Single().AddonGroups.Single().Choices.Single().Id;

        await using (var seedContext = database.CreateContext())
        {
            await CheckoutDraftTestSupport.SeedSnapshotAsync(seedContext, snapshot);
            var service = CreateService(seedContext, timeProvider);
            var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
            request.Items =
            [
                new CheckoutDraftItemRequest
                {
                    OfferingSourceId = snapshot.Categories.Single().Offerings.Single().Id,
                    Selections =
                    [
                        new CheckoutDraftSelectionRequest
                        {
                            AddonChoiceSourceId = choiceId,
                            Quantity = 1
                        }
                    ]
                }
            ];

            await service.CreateAsync(
                request,
                deviceId,
                requestedLanguage: "ar",
                acceptLanguageHeader: "ar",
                CancellationToken.None);
        }

        await using var context = database.CreateContext();
        var storedItem = await context.CheckoutDraftItems
            .Include(item => item.Selections)
            .SingleAsync();
        var originalSelection = storedItem.Selections.Single();

        context.CheckoutDraftSelections.Add(new CheckoutDraftSelection
        {
            Id = Guid.NewGuid(),
            CheckoutDraftItemId = storedItem.Id,
            AddonGroupSourceId = originalSelection.AddonGroupSourceId,
            AddonChoiceSourceId = originalSelection.AddonChoiceSourceId,
            Quantity = 1,
            DisplayOrder = originalSelection.DisplayOrder + 1
        });

        var action = () => context.SaveChangesAsync();

        await action.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CheckoutDrafts_WhenUpdateReplacesItemsAndSelections_PersistsOnlyTheLatestChildren()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var snapshot = CreateTwoOfferingSnapshot(Guid.NewGuid());
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var deviceId = Guid.NewGuid();
        var firstOffering = snapshot.Categories.Single().Offerings.First();
        var secondOffering = snapshot.Categories.Single().Offerings.Last();
        Guid orderGuid;

        await using (var seedContext = database.CreateContext())
        {
            await CheckoutDraftTestSupport.SeedSnapshotAsync(seedContext, snapshot);
            var service = CreateService(seedContext, timeProvider);
            var createRequest = CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
            createRequest.Items =
            [
                new CheckoutDraftItemRequest
                {
                    OfferingSourceId = firstOffering.Id
                }
            ];

            var created = await service.CreateAsync(
                createRequest,
                deviceId,
                requestedLanguage: "ar",
                acceptLanguageHeader: "ar",
                CancellationToken.None);
            orderGuid = created.OrderGuid;
        }

        timeProvider.Advance(TimeSpan.FromMinutes(5));

        await using (var updateContext = database.CreateContext())
        {
            var service = CreateService(updateContext, timeProvider);
            var updateRequest = CheckoutDraftTestSupport.CreateValidUpdateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
                expectedVersion: 1);
            updateRequest.Items =
            [
                new CheckoutDraftItemRequest
                {
                    OfferingSourceId = secondOffering.Id
                }
            ];

            var updated = await service.UpdateAsync(
                orderGuid,
                updateRequest,
                deviceId,
                requestedLanguage: "ar",
                acceptLanguageHeader: "ar",
                CancellationToken.None);

            updated.Version.Should().Be(2);
        }

        await using var verificationContext = database.CreateContext();
        var storedDraft = await verificationContext.CheckoutDrafts
            .Include(draft => draft.Items)
                .ThenInclude(item => item.Selections)
            .SingleAsync();

        storedDraft.Items.Should().ContainSingle();
        storedDraft.Items.Single().OfferingSourceId.Should().Be(secondOffering.Id);
        storedDraft.Items.Single().Selections.Should().BeEmpty();
    }

    private static CheckoutDraftService CreateService(
        ApplicationDbContext context,
        ManualTimeProvider timeProvider)
    {
        var businessApiClient = new ScriptedBusinessApiClient
        {
            GetCatalogSnapshotHandler = (companyId, _) =>
                Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId, version: 99))
        };
        var catalogOptionsMonitor = new Mock<IOptionsMonitor<CatalogReadModelOptions>>();
        catalogOptionsMonitor.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new CatalogReadModelOptions
            {
                FreshWindowSeconds = 300,
                MaxStaleWindowSeconds = 3600,
                LeaseDurationSeconds = 60
            });
        var refreshCoordinator = new CatalogProviderRefreshCoordinator(
            new CatalogReadModelRepository(context),
            businessApiClient,
            catalogOptionsMonitor.Object,
            timeProvider,
            new TestAppLogger());

        return new CheckoutDraftService(
            new CheckoutDraftRepository(context),
            new CatalogReadModelRepository(context),
            refreshCoordinator,
            timeProvider,
            Options.Create(new CheckoutDraftOptions
            {
                LifetimeMinutes = 30
            }),
            new TestAppLogger());
    }

    private static CatalogSnapshotResponse CreateTwoOfferingSnapshot(Guid companyId)
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(companyId, version: 14);
        var category = snapshot.Categories.Single();
        var firstOffering = category.Offerings.Single();
        firstOffering.AddonGroups.Single().Choices.Single().DurationAdjustmentMinutes = 0;
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
}
