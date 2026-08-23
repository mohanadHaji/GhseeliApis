using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Data.Common;

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

    [Fact]
    public async Task CheckoutDraftPricing_WhenConcurrentRepricesTargetSameDraft_OneWinsAndSnapshotPersistsOnce()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 21);
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var deviceId = Guid.NewGuid();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var validationCalls = 0;
        Guid orderGuid;

        var seedBusinessClient = new ScriptedBusinessApiClient
        {
            GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(snapshot),
            ValidateAppointmentHandler = (request, _, _) =>
                Task.FromResult(CatalogTestSupport.CreateValidationResponse(snapshot, request))
        };

        await using (var seedContext = database.CreateContext())
        {
            await CheckoutDraftTestSupport.SeedSnapshotAsync(seedContext, snapshot);
            var service = CreateService(seedContext, timeProvider, seedBusinessClient);
            var created = await service.CreateAsync(
                CheckoutDraftTestSupport.CreateValidCreateRequest(
                    snapshot,
                    new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
                deviceId,
                requestedLanguage: "ar",
                acceptLanguageHeader: "ar",
                CancellationToken.None);
            orderGuid = created.OrderGuid;
        }

        var pricingBusinessClient = new ScriptedBusinessApiClient
        {
            GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(snapshot),
            ValidateAppointmentHandler = async (request, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref validationCalls) == 2)
                {
                    entered.TrySetResult(true);
                }

                await release.Task.WaitAsync(cancellationToken);
                return CatalogTestSupport.CreateValidationResponse(snapshot, request);
            }
        };

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstService = CreatePricingService(firstContext, timeProvider, pricingBusinessClient);
        var secondService = CreatePricingService(secondContext, timeProvider, pricingBusinessClient);

        var firstTask = firstService.RepriceDraftAsync(
            orderGuid,
            new RepriceCheckoutDraftRequest
            {
                ExpectedVersion = 1
            },
            deviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        var secondTask = secondService.RepriceDraftAsync(
            orderGuid,
            new RepriceCheckoutDraftRequest
            {
                ExpectedVersion = 1
            },
            deviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        release.TrySetResult(true);

        var tasks = new[] { firstTask, secondTask };
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
        }

        var successes = tasks.Where(task => task.Status == TaskStatus.RanToCompletion).ToArray();
        var failures = tasks.Where(task => task.IsFaulted).ToArray();

        successes.Should().ContainSingle();
        failures.Should().ContainSingle();
        var failure = failures.Single().Exception!.Flatten().InnerExceptions.Single();
        failure.Should().BeOfType<CheckoutPricingException>()
            .Which.Code.Should().Be(CheckoutDraftProblemCodes.VersionConflict);

        await using var verificationContext = database.CreateContext();
        var storedDraft = await verificationContext.CheckoutDrafts
            .Include(draft => draft.PricingSnapshot)
                .ThenInclude(snapshotRow => snapshotRow!.Items)
            .SingleAsync();
        storedDraft.PublicVersion.Should().Be(2);
        storedDraft.RequiresReprice.Should().BeFalse();
        storedDraft.PricingSnapshot.Should().NotBeNull();
        storedDraft.PricingSnapshot!.Items.Should().ContainSingle();
        (await verificationContext.CheckoutDraftPricingSnapshots.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CheckoutDraftPricing_WhenRepricedTwice_ReplacesSnapshotWithoutDuplicateRows()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 22);
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var deviceId = Guid.NewGuid();
        var businessClient = new ScriptedBusinessApiClient
        {
            GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(snapshot),
            ValidateAppointmentHandler = (request, _, _) =>
                Task.FromResult(CatalogTestSupport.CreateValidationResponse(snapshot, request))
        };
        Guid orderGuid;

        await using (var seedContext = database.CreateContext())
        {
            await CheckoutDraftTestSupport.SeedSnapshotAsync(seedContext, snapshot);
            var service = CreateService(seedContext, timeProvider, businessClient);
            var created = await service.CreateAsync(
                CheckoutDraftTestSupport.CreateValidCreateRequest(
                    snapshot,
                    new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
                deviceId,
                requestedLanguage: "ar",
                acceptLanguageHeader: "ar",
                CancellationToken.None);
            orderGuid = created.OrderGuid;
        }

        await using (var firstPricingContext = database.CreateContext())
        {
            var pricingService = CreatePricingService(firstPricingContext, timeProvider, businessClient);
            await pricingService.RepriceDraftAsync(
                orderGuid,
                new RepriceCheckoutDraftRequest
                {
                    ExpectedVersion = 1
                },
                deviceId,
                requestedLanguage: "ar",
                acceptLanguageHeader: "ar",
                CancellationToken.None);
        }

        timeProvider.Advance(TimeSpan.FromMinutes(1));

        await using (var secondPricingContext = database.CreateContext())
        {
            var pricingService = CreatePricingService(secondPricingContext, timeProvider, businessClient);
            await pricingService.RepriceDraftAsync(
                orderGuid,
                new RepriceCheckoutDraftRequest
                {
                    ExpectedVersion = 2
                },
                deviceId,
                requestedLanguage: "ar",
                acceptLanguageHeader: "ar",
                CancellationToken.None);
        }

        await using var verificationContext = database.CreateContext();
        var storedDraft = await verificationContext.CheckoutDrafts
            .Include(draft => draft.PricingSnapshot)
                .ThenInclude(snapshotRow => snapshotRow!.Items)
            .SingleAsync();
        storedDraft.PublicVersion.Should().Be(3);
        (await verificationContext.CheckoutDraftPricingSnapshots.CountAsync()).Should().Be(1);
        storedDraft.PricingSnapshot.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckoutDraftRepository_WhenCommitFailsTransiently_RetriesEntireSaveWithoutPartialState()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 23);
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var deviceId = Guid.NewGuid();

        await using (var seedContext = database.CreateContext())
        {
            await CheckoutDraftTestSupport.SeedSnapshotAsync(seedContext, snapshot);
            await CreateService(seedContext, timeProvider).CreateAsync(
                CheckoutDraftTestSupport.CreateValidCreateRequest(
                    snapshot,
                    new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
                deviceId,
                requestedLanguage: "ar",
                acceptLanguageHeader: "ar",
                CancellationToken.None);
        }

        var commitInterceptor = new FailFirstCommitInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(database.ConnectionString)
            .ReplaceService<IExecutionStrategyFactory, TestRetryingExecutionStrategyFactory>()
            .AddInterceptors(commitInterceptor)
            .Options;
        await using var context = new ApplicationDbContext(options);
        var repository = new CheckoutDraftRepository(context);
        var draft = await repository.GetOwnedByOrderGuidAsync(
            (await context.CheckoutDrafts.Select(row => row.OrderGuid).SingleAsync()),
            deviceId,
            CancellationToken.None);
        draft.Should().NotBeNull();
        draft!.PublicVersion++;
        draft.RequiresReprice = false;
        repository.AddPricingSnapshot(new CheckoutDraftPricingSnapshot
        {
            CheckoutDraftId = draft.Id,
            CatalogVersion = snapshot.CatalogVersion,
            Currency = "ILS",
            QuotedAtUtc = timeProvider.GetUtcNow(),
            ServiceFeeMode = CheckoutServiceFeeMode.None
        });

        await repository.ExecuteInTransactionAsync(
            cancellationToken => repository.SaveChangesAsync(cancellationToken),
            new CheckoutDraftPricingPersistenceExpectation(
                draft.Id,
                draft.PublicVersion,
                draft.PricingSnapshot!.Id),
            CancellationToken.None);

        commitInterceptor.CommitAttempts.Should().Be(2);
        await using var verificationContext = database.CreateContext();
        var storedDraft = await verificationContext.CheckoutDrafts.SingleAsync();
        storedDraft.PublicVersion.Should().Be(2);
        storedDraft.RequiresReprice.Should().BeFalse();
        (await verificationContext.CheckoutDraftPricingSnapshots.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CheckoutDraftRepository_WhenCommitSucceedsButAcknowledgementFails_VerifiesPersistedPricingWithoutRetry()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 24);
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var deviceId = Guid.NewGuid();

        await using (var seedContext = database.CreateContext())
        {
            await CheckoutDraftTestSupport.SeedSnapshotAsync(seedContext, snapshot);
            await CreateService(seedContext, timeProvider).CreateAsync(
                CheckoutDraftTestSupport.CreateValidCreateRequest(
                    snapshot,
                    new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
                deviceId,
                requestedLanguage: "ar",
                acceptLanguageHeader: "ar",
                CancellationToken.None);
        }

        var commitInterceptor = new FailFirstCommitAcknowledgementInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(database.ConnectionString)
            .ReplaceService<IExecutionStrategyFactory, TestRetryingExecutionStrategyFactory>()
            .AddInterceptors(commitInterceptor)
            .Options;
        await using var context = new ApplicationDbContext(options);
        var repository = new CheckoutDraftRepository(context);
        var draft = await repository.GetOwnedByOrderGuidAsync(
            await context.CheckoutDrafts.Select(row => row.OrderGuid).SingleAsync(),
            deviceId,
            CancellationToken.None);
        draft.Should().NotBeNull();
        draft!.PublicVersion++;
        draft.RequiresReprice = false;
        var pricingSnapshot = new CheckoutDraftPricingSnapshot
        {
            CheckoutDraftId = draft.Id,
            CatalogVersion = snapshot.CatalogVersion,
            Currency = "ILS",
            QuotedAtUtc = timeProvider.GetUtcNow(),
            ServiceFeeMode = CheckoutServiceFeeMode.None
        };
        repository.AddPricingSnapshot(pricingSnapshot);

        await repository.ExecuteInTransactionAsync(
            cancellationToken => repository.SaveChangesAsync(cancellationToken),
            new CheckoutDraftPricingPersistenceExpectation(
                draft.Id,
                draft.PublicVersion,
                pricingSnapshot.Id),
            CancellationToken.None);

        commitInterceptor.CommitAttempts.Should().Be(1);
        await using var verificationContext = database.CreateContext();
        var storedDraft = await verificationContext.CheckoutDrafts
            .Include(row => row.PricingSnapshot)
            .SingleAsync();
        storedDraft.PublicVersion.Should().Be(2);
        storedDraft.PricingSnapshot.Should().NotBeNull();
        storedDraft.PricingSnapshot!.Id.Should().Be(pricingSnapshot.Id);
    }

    private static CheckoutDraftService CreateService(
        ApplicationDbContext context,
        ManualTimeProvider timeProvider,
        ScriptedBusinessApiClient? businessApiClient = null)
    {
        businessApiClient ??= new ScriptedBusinessApiClient
        {
            GetCatalogSnapshotHandler = (companyId, _) =>
                Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId, version: 99)),
            ValidateAppointmentHandler = (request, _, _) =>
                Task.FromResult(CatalogTestSupport.CreateValidationResponse(
                    CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 99),
                    request))
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

    private static CheckoutPricingService CreatePricingService(
        ApplicationDbContext context,
        ManualTimeProvider timeProvider,
        ScriptedBusinessApiClient businessApiClient)
    {
        var catalogOptionsMonitor = new Mock<IOptionsMonitor<CatalogReadModelOptions>>();
        catalogOptionsMonitor.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new CatalogReadModelOptions
            {
                FreshWindowSeconds = 300,
                MaxStaleWindowSeconds = 3600,
                LeaseDurationSeconds = 60
            });
        var stripeOptionsMonitor = new Mock<IOptionsMonitor<StripeConfigurationOptions>>();
        stripeOptionsMonitor.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new StripeConfigurationOptions());
        var refreshCoordinator = new CatalogProviderRefreshCoordinator(
            new CatalogReadModelRepository(context),
            businessApiClient,
            catalogOptionsMonitor.Object,
            timeProvider,
            new TestAppLogger());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[InternalServiceWireConstants.CorrelationIdHeaderName] = "corr-step11-relational";

        return new CheckoutPricingService(
            new CheckoutDraftRepository(context),
            new CatalogReadModelRepository(context),
            refreshCoordinator,
            businessApiClient,
            new CheckoutPaymentCapabilitiesService(stripeOptionsMonitor.Object),
            new HttpContextAccessor
            {
                HttpContext = httpContext
            },
            timeProvider,
            Options.Create(new CheckoutPricingOptions()),
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

    private sealed class TestTransientException : Exception;

    private sealed class FailFirstCommitInterceptor : DbTransactionInterceptor
    {
        private int _commitAttempts;

        public int CommitAttempts => _commitAttempts;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _commitAttempts) == 1)
            {
                throw new TestTransientException();
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailFirstCommitAcknowledgementInterceptor : DbTransactionInterceptor
    {
        private int _commitAttempts;

        public int CommitAttempts => _commitAttempts;

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _commitAttempts) == 1)
            {
                throw new TestTransientException();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TestRetryingExecutionStrategyFactory : IExecutionStrategyFactory
    {
        private readonly ExecutionStrategyDependencies _dependencies;

        public TestRetryingExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        {
            _dependencies = dependencies;
        }

        public IExecutionStrategy Create() => new TestRetryingExecutionStrategy(_dependencies);
    }

    private sealed class TestRetryingExecutionStrategy : ExecutionStrategy
    {
        public TestRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
            : base(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
        {
        }

        protected override bool ShouldRetryOn(Exception exception) =>
            exception is TestTransientException;
    }
}
