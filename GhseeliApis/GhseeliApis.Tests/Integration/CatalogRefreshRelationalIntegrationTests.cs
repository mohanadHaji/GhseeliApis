using FluentAssertions;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using System.Data.Common;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Verifies SQL Server refresh atomicity, retry-strategy transactions, and provider lease concurrency.
/// </summary>
public class CatalogRefreshRelationalIntegrationTests
{
    [Fact]
    public async Task GetBusinessesAsync_WhenRetryStrategyEnabled_PerformsInitialRefreshWithoutExecutionStrategyErrors()
    {
        await using var harness = await RelationalCatalogHarness.CreateAsync();
        harness.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId, version: 1));

        using var scope = harness.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogReadModelService>();

        var response = await service.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Businesses.Should().ContainSingle();
        response.Businesses.Single().Catalog.Version.Should().Be(1);
    }

    [Fact]
    public async Task GetBusinessesAsync_WhenConcurrentRequestsRefreshSameProvider_UpstreamExecutesOnce()
    {
        await using var harness = await RelationalCatalogHarness.CreateAsync();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.BusinessApiClient.GetCatalogSnapshotHandler = async (companyId, cancellationToken) =>
        {
            entered.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return CatalogTestSupport.CreateSnapshot(companyId, version: 1);
        };

        using var firstScope = harness.Services.CreateScope();
        using var secondScope = harness.Services.CreateScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<ICatalogReadModelService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<ICatalogReadModelService>();

        var firstTask = firstService.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondTask = secondService.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        await Task.Delay(200);
        release.TrySetResult(true);

        var responses = await Task.WhenAll(firstTask, secondTask);

        responses.Should().OnlyContain(response => response.Businesses.Count == 1);
        harness.BusinessApiClient.CatalogSnapshotRequests.Should().Be(1);
    }

    [Fact]
    public async Task GetBusinessesAsync_WhenRefreshFails_ReleasesLeaseForImmediateRetry()
    {
        await using var harness = await RelationalCatalogHarness.CreateAsync();
        harness.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
        {
            if (harness.BusinessApiClient.CatalogSnapshotRequests == 1)
            {
                throw new BusinessApiUnavailableException("Unavailable", "corr-step9-lease");
            }

            return Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId, version: 2));
        };

        using var firstScope = harness.Services.CreateScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<ICatalogReadModelService>();
        var firstAction = () => firstService.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await firstAction.Should().ThrowAsync<CatalogReadModelException>()
            .Where(exception => exception.Code == CatalogProblemCodes.Unavailable);

        using var secondScope = harness.Services.CreateScope();
        var secondService = secondScope.ServiceProvider.GetRequiredService<ICatalogReadModelService>();
        var response = await secondService.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Businesses.Should().ContainSingle();
        harness.BusinessApiClient.CatalogSnapshotRequests.Should().Be(2);
    }

    [Fact]
    public async Task GetBusinessesAsync_WhenLeaseExpiresAndRefreshIsHandedOff_ReturnsWinningSnapshot()
    {
        await using var harness = await RelationalCatalogHarness.CreateAsync(leaseDurationSeconds: 1);
        var enteredFirstCall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCall = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = 0;
        harness.BusinessApiClient.GetCatalogSnapshotHandler = async (companyId, cancellationToken) =>
        {
            var currentInvocation = Interlocked.Increment(ref invocation);
            if (currentInvocation == 1)
            {
                enteredFirstCall.TrySetResult(true);
                await releaseFirstCall.Task.WaitAsync(cancellationToken);

                return CatalogTestSupport.CreateSnapshot(
                    companyId,
                    version: 3,
                    companyNameAr: "الطلب الأول");
            }

            return CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 4,
                companyNameAr: "الطلب الفائز");
        };

        using var firstScope = harness.Services.CreateScope();
        using var secondScope = harness.Services.CreateScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<ICatalogReadModelService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<ICatalogReadModelService>();

        var firstTask = firstService.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        await enteredFirstCall.Task.WaitAsync(TimeSpan.FromSeconds(10));

        harness.TimeProvider.Advance(TimeSpan.FromSeconds(2));
        var secondResponse = await secondService.GetBusinessesAsync(
            new GetCatalogBusinessesRequest(),
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        releaseFirstCall.TrySetResult(true);
        var firstResponse = await firstTask;

        firstResponse.Businesses.Should().ContainSingle();
        secondResponse.Businesses.Should().ContainSingle();
        firstResponse.Businesses.Single().Name.Should().Be("الطلب الفائز");
        secondResponse.Businesses.Single().Name.Should().Be("الطلب الفائز");
        firstResponse.Businesses.Single().Catalog.Version.Should().Be(4);
        secondResponse.Businesses.Single().Catalog.Version.Should().Be(4);
        harness.BusinessApiClient.CatalogSnapshotRequests.Should().Be(2);
    }

    [Fact]
    public async Task ApplySnapshotAsync_WhenPersistenceFails_RollsBackExistingGraph()
    {
        await using var harness = await RelationalCatalogHarness.CreateAsync();
        harness.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId, version: 3));

        using (var seedScope = harness.Services.CreateScope())
        {
            var service = seedScope.ServiceProvider.GetRequiredService<ICatalogReadModelService>();
            await service.GetBusinessesAsync(
                new GetCatalogBusinessesRequest(),
                acceptLanguageHeader: "ar",
                CancellationToken.None);
        }

        Guid providerId;
        long originalVersion;
        string originalBusinessName;
        using (var scope = harness.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var provider = await context.CatalogProviders.SingleAsync();
            providerId = provider.Id;
            originalVersion = provider.CatalogVersion;
            originalBusinessName = provider.NameAr;
            provider.RefreshLeaseToken = "lease-step9";
            provider.RefreshLeaseAcquiredAtUtc = DateTimeOffset.UtcNow;
            provider.RefreshLeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1);
            await context.SaveChangesAsync();
        }

        using (var failingScope = harness.Services.CreateScope())
        {
            var repository = (CatalogReadModelRepository)failingScope.ServiceProvider
                .GetRequiredService<ICatalogReadModelRepository>();

            var action = () => repository.ApplySnapshotAsync(
                providerId,
                CatalogTestSupport.CreateSnapshot(
                    harness.SourceCompanyId,
                    version: 4,
                    companyNameAr: new string('س', 250)),
                snapshotHash: "invalid-step9-hash",
                refreshedAtUtc: DateTimeOffset.UtcNow,
                leaseToken: "lease-step9",
                CancellationToken.None);

            await action.Should().ThrowAsync<DbUpdateException>();
        }

        using (var verificationScope = harness.Services.CreateScope())
        {
            var context = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var provider = await context.CatalogProviders.SingleAsync();
            provider.CatalogVersion.Should().Be(originalVersion);
            provider.NameAr.Should().Be(originalBusinessName);
        }
    }

    [Fact]
    public async Task SynchronizeConfiguredProvidersAsync_WhenConcurrentFirstWritersSeedSameProvider_DoesNotDuplicateRows()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var sourceCompanyId = Guid.NewGuid();
        CatalogProviderRegistrationOptions[] configuredProviders =
        [
            new CatalogProviderRegistrationOptions
            {
                SourceCompanyId = sourceCompanyId,
                Enabled = true,
                Order = 0
            }
        ];

        await using var contextOne = database.CreateContext();
        await using var contextTwo = database.CreateContext();
        var repositoryOne = new CatalogReadModelRepository(contextOne);
        var repositoryTwo = new CatalogReadModelRepository(contextTwo);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = Task.Run(async () =>
        {
            await gate.Task;
            await repositoryOne.SynchronizeConfiguredProvidersAsync(
                configuredProviders,
                CancellationToken.None);
        });

        var secondTask = Task.Run(async () =>
        {
            await gate.Task;
            await repositoryTwo.SynchronizeConfiguredProvidersAsync(
                configuredProviders,
                CancellationToken.None);
        });

        gate.TrySetResult(true);
        await Task.WhenAll(firstTask, secondTask);

        await using var verificationContext = database.CreateContext();
        var providers = await verificationContext.CatalogProviders
            .Where(provider => provider.SourceCompanyId == sourceCompanyId)
            .ToListAsync();

        providers.Should().ContainSingle();
        providers.Single().IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SynchronizeConfiguredProvidersAsync_WhenConfigurationsConverge_DoesNotLeakDuplicatesOrErrors()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var firstSourceCompanyId = Guid.NewGuid();
        var secondSourceCompanyId = Guid.NewGuid();

        await using var contextOne = database.CreateContext();
        await using var contextTwo = database.CreateContext();
        var repositoryOne = new CatalogReadModelRepository(contextOne);
        var repositoryTwo = new CatalogReadModelRepository(contextTwo);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = Task.Run(async () =>
        {
            await gate.Task;
            await repositoryOne.SynchronizeConfiguredProvidersAsync(
            [
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
                }
            ],
            CancellationToken.None);
        });

        var secondTask = Task.Run(async () =>
        {
            await gate.Task;
            await repositoryTwo.SynchronizeConfiguredProvidersAsync(
            [
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = firstSourceCompanyId,
                    Enabled = true,
                    Order = 5
                }
            ],
            CancellationToken.None);
        });

        gate.TrySetResult(true);
        await Task.WhenAll(firstTask, secondTask);

        await using var verificationContext = database.CreateContext();
        var providers = await verificationContext.CatalogProviders
            .Where(provider =>
                provider.SourceCompanyId == firstSourceCompanyId ||
                provider.SourceCompanyId == secondSourceCompanyId)
            .OrderBy(provider => provider.SourceCompanyId)
            .ToListAsync();

        providers.Should().HaveCount(2);
        providers.Select(provider => provider.SourceCompanyId)
            .Should()
            .OnlyHaveUniqueItems();
        providers.Count(provider => provider.SourceCompanyId == firstSourceCompanyId).Should().Be(1);
        providers.Count(provider => provider.SourceCompanyId == secondSourceCompanyId).Should().Be(1);
    }

    [Fact]
    public async Task GetEnabledProvidersWithGraphAsync_UsesSingleRelationalSelect()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var sourceCompanyId = Guid.NewGuid();
        await database.ExecuteAsync(context =>
        {
            context.CatalogProviders.Add(CreateSeedProvider(sourceCompanyId));
        });

        var interceptor = new TaggedReaderCountInterceptor(
            "CatalogReadModelRepository.GetEnabledProvidersWithGraphAsync");

        await using var context = database.CreateContext(interceptor);
        var repository = new CatalogReadModelRepository(context);

        var providers = await repository.GetEnabledProvidersWithGraphAsync(
            null,
            CancellationToken.None);

        providers.Should().ContainSingle();
        interceptor.TaggedReaderExecutions.Should().Be(1);
    }

    [Fact]
    public async Task EnabledCatalogReads_ExcludeNonCarWashProviders()
    {
        await using var database = await SqlServerCatalogDatabase.CreateAsync();
        var carWash = CreateSeedProvider(Guid.NewGuid());
        var future = CreateSeedProvider(Guid.NewGuid());
        future.BusinessVerticalCode = "mechanics";
        await database.ExecuteAsync(context =>
        {
            context.CatalogProviders.AddRange(carWash, future);
        });

        await using var context = database.CreateContext();
        var repository = new CatalogReadModelRepository(context);

        var summaries = await repository.ListEnabledProviderSummariesAsync(
            CancellationToken.None);
        var graph = await repository.GetEnabledProvidersWithGraphAsync(
            null,
            CancellationToken.None);

        summaries.Should().ContainSingle(provider => provider.Id == carWash.Id);
        graph.Should().ContainSingle(provider => provider.Id == carWash.Id);
    }

    private static CatalogProviderReadModel CreateSeedProvider(Guid sourceCompanyId)
    {
        var provider = new CatalogProviderReadModel
        {
            Id = Guid.NewGuid(),
            SourceCompanyId = sourceCompanyId,
            IsEnabled = true,
            DisplayOrder = 0,
            NameAr = "مغسلة",
            CatalogVersion = 2,
            SnapshotHash = "hash",
            SnapshotGeneratedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-6),
            LastSuccessfulRefreshAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var branch = new CatalogBranchReadModel
        {
            Id = Guid.NewGuid(),
            SourceBranchId = Guid.NewGuid(),
            Provider = provider,
            ProviderId = provider.Id,
            NameAr = "الفرع",
            AddressAr = "العنوان",
            HasPublishedServiceArea = true,
            UsesBranchCoordinates = true,
            ServiceAreaCenterLatitude = 32.1,
            ServiceAreaCenterLongitude = 34.8,
            ServiceAreaRadiusKm = 10,
            Latitude = 32.1,
            Longitude = 34.8
        };
        var category = new CatalogCategoryReadModel
        {
            Id = Guid.NewGuid(),
            SourceCategoryId = Guid.NewGuid(),
            Provider = provider,
            ProviderId = provider.Id,
            NameAr = "الفئة",
            DisplayOrder = 1
        };
        var offering = new CatalogOfferingReadModel
        {
            Id = Guid.NewGuid(),
            SourceOfferingId = Guid.NewGuid(),
            Category = category,
            CategoryId = category.Id,
            Branch = branch,
            BranchId = branch.Id,
            NameAr = "الخدمة",
            BasePrice = 25,
            DurationMinutes = 20,
            DisplayOrder = 1
        };
        var addonGroup = new CatalogAddonGroupReadModel
        {
            Id = Guid.NewGuid(),
            SourceAddonGroupId = Guid.NewGuid(),
            Offering = offering,
            OfferingId = offering.Id,
            NameAr = "إضافات",
            SelectionType = "Multiple",
            DisplayOrder = 0
        };
        var addonChoice = new CatalogAddonChoiceReadModel
        {
            Id = Guid.NewGuid(),
            SourceAddonChoiceId = Guid.NewGuid(),
            AddonGroup = addonGroup,
            AddonGroupId = addonGroup.Id,
            NameAr = "شمع",
            DisplayOrder = 0
        };

        provider.Branches.Add(branch);
        provider.Categories.Add(category);
        category.Offerings.Add(offering);
        offering.AddonGroups.Add(addonGroup);
        addonGroup.Choices.Add(addonChoice);

        return provider;
    }
}

internal sealed class RelationalCatalogHarness : IAsyncDisposable
{
    private readonly Mock<IOptionsMonitor<CatalogReadModelOptions>> _optionsMonitor = new();
    private readonly SqlServerCatalogDatabase _database;
    private readonly IInterceptor[] _interceptors;

    private RelationalCatalogHarness(
        SqlServerCatalogDatabase database,
        double leaseDurationSeconds,
        IInterceptor[] interceptors)
    {
        _database = database;
        _interceptors = interceptors;

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(_database.ConnectionString, sqlServerOptions =>
            {
                sqlServerOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
                sqlServerOptions.CommandTimeout(60);
                sqlServerOptions.UseCompatibilityLevel(120);
            });

            if (_interceptors.Length > 0)
            {
                options.AddInterceptors(_interceptors);
            }
        });
        services.AddScoped<ICatalogReadModelRepository, CatalogReadModelRepository>();
        services.AddScoped<ICatalogProviderRefreshCoordinator, CatalogProviderRefreshCoordinator>();
        services.AddScoped<ICatalogReadModelService, CatalogReadModelService>();
        services.AddSingleton<IBusinessApiClient>(BusinessApiClient);
        services.AddSingleton<TimeProvider>(TimeProvider);
        services.AddSingleton<IOptionsMonitor<CatalogReadModelOptions>>(_optionsMonitor.Object);
        services.AddSingleton<Ghseeli.Common.Logging.IAppLogger, TestAppLogger>();

        _optionsMonitor.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new CatalogReadModelOptions
            {
                FreshWindowSeconds = 60,
                MaxStaleWindowSeconds = 300,
                LeaseDurationSeconds = leaseDurationSeconds,
                Providers =
                [
                    new CatalogProviderRegistrationOptions
                    {
                        SourceCompanyId = SourceCompanyId,
                        Enabled = true,
                        Order = 0
                    }
                ]
            });

        Services = services.BuildServiceProvider();
    }

    public Guid SourceCompanyId { get; } = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public ScriptedBusinessApiClient BusinessApiClient { get; } = new();
    public ManualTimeProvider TimeProvider { get; } = new(new DateTimeOffset(2026, 8, 21, 18, 0, 0, TimeSpan.Zero));
    public ServiceProvider Services { get; }

    public static async Task<RelationalCatalogHarness> CreateAsync(
        double leaseDurationSeconds = 15,
        params IInterceptor[] interceptors)
    {
        var database = await SqlServerCatalogDatabase.CreateAsync();
        return new RelationalCatalogHarness(database, leaseDurationSeconds, interceptors);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _database.DisposeAsync();
    }
}

internal sealed class TaggedReaderCountInterceptor : DbCommandInterceptor
{
    private readonly string _tag;
    private int _taggedReaderExecutions;

    public TaggedReaderCountInterceptor(string tag)
    {
        _tag = tag;
    }

    public int TaggedReaderExecutions => _taggedReaderExecutions;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Count(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Count(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Count(DbCommand command)
    {
        if (command.CommandText.Contains(_tag, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _taggedReaderExecutions);
        }
    }
}
