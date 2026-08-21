using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace GhseeliApis.Tests.Services.Checkout;

/// <summary>
/// Defines anonymous checkout draft create, read, and update behavior.
/// </summary>
public class CheckoutDraftServiceTests
{
    private const string SingleChoiceOfferingReferenceCode = "SINGLE-01";
    private const string QuantityCounterOfferingReferenceCode = "QUANTITY-01";
    private const string FixedIncludedOfferingReferenceCode = "FIXED-01";

    [Fact]
    public async Task CreateAsync_PersistsNormalizedDraftAndCapturesCatalogVersion()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 7);
        await using var harness = await CreateHarnessAsync(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 13, 0, 0, TimeSpan.FromHours(3)));

        var response = await harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "he",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Language.Should().Be("he");
        response.OrderGuid.Should().NotBe(Guid.Empty);
        response.Version.Should().Be(1);
        response.RequiresReprice.Should().BeTrue();
        response.Intent.CatalogVersion.Should().Be(7);
        response.Intent.RequestedSlotStartUtc.Offset.Should().Be(TimeSpan.Zero);
        response.Intent.RequestedSlotStartUtc.Should().Be(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        response.Intent.Items.Should().ContainSingle();
        response.Intent.Items.Single().Selections.Should().BeEmpty();
        response.ExpiresAt.Should().Be(harness.TimeProvider.GetUtcNow().AddMinutes(30));

        var persistedDraft = await harness.Context.CheckoutDrafts
            .Include(draft => draft.Items)
                .ThenInclude(item => item.Selections)
            .SingleAsync();
        persistedDraft.PublicVersion.Should().Be(1);
        persistedDraft.CatalogVersion.Should().Be(7);
        persistedDraft.RequestedSlotStartUtc.Should().Be(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        persistedDraft.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAsync_WhenOwnedByDifferentDevice_ThrowsLocalizedNotFound()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 2);
        await using var harness = await CreateHarnessAsync(snapshot);
        var created = await harness.Service.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        var action = () => harness.Service.GetAsync(
            created.OrderGuid,
            Guid.NewGuid(),
            requestedLanguage: "he",
            acceptLanguageHeader: "he",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception =>
                exception.Code == CheckoutDraftProblemCodes.NotFound &&
                exception.StatusCode == StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task GetAsync_WhenExpired_ThrowsGoneAndDoesNotReviveDraft()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 3);
        await using var harness = await CreateHarnessAsync(snapshot);
        var created = await harness.Service.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        harness.TimeProvider.Advance(TimeSpan.FromMinutes(30));

        var action = () => harness.Service.GetAsync(
            created.OrderGuid,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception =>
                exception.Code == CheckoutDraftProblemCodes.Expired &&
                exception.StatusCode == StatusCodes.Status410Gone);
    }

    [Fact]
    public async Task UpdateAsync_WhenExpiredAtBoundary_CannotReviveDraft()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 3);
        await using var harness = await CreateHarnessAsync(snapshot);
        var created = await harness.Service.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        harness.TimeProvider.Advance(TimeSpan.FromMinutes(30));

        var action = () => harness.Service.UpdateAsync(
            created.OrderGuid,
            CheckoutDraftTestSupport.CreateValidUpdateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
                expectedVersion: 1),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception =>
                exception.Code == CheckoutDraftProblemCodes.Expired &&
                exception.StatusCode == StatusCodes.Status410Gone);

        (await harness.Context.CheckoutDrafts.Select(draft => draft.PublicVersion).SingleAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_WhenExpectedVersionIsStale_ThrowsConflict()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 4);
        await using var harness = await CreateHarnessAsync(snapshot);
        var created = await harness.Service.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        var request = CheckoutDraftTestSupport.CreateValidUpdateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
            expectedVersion: created.Version + 1);

        var action = () => harness.Service.UpdateAsync(
            created.OrderGuid,
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception =>
                exception.Code == CheckoutDraftProblemCodes.VersionConflict &&
                exception.StatusCode == StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task UpdateAsync_WhenExpectedVersionMatches_SucceedsAndIncrementsExactlyOnce()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 4);
        await using var harness = await CreateHarnessAsync(snapshot);
        var created = await harness.Service.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);
        (await harness.Context.CheckoutDrafts.Select(draft => draft.PublicVersion).SingleAsync()).Should().Be(1);
        harness.TimeProvider.Advance(TimeSpan.FromMinutes(5));

        var updated = await harness.Service.UpdateAsync(
            created.OrderGuid,
            CheckoutDraftTestSupport.CreateValidUpdateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
                expectedVersion: 1),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        updated.Version.Should().Be(2);
        updated.Intent.RequestedSlotStartUtc.Should().Be(new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero));
        updated.ExpiresAt.Should().Be(harness.TimeProvider.GetUtcNow().AddMinutes(30));

        var stored = await harness.Context.CheckoutDrafts.SingleAsync();
        stored.PublicVersion.Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_WhenCatalogVersionChanges_RecapturesLatestCatalogVersion()
    {
        var companyId = Guid.NewGuid();
        var initialSnapshot = CatalogTestSupport.CreateSnapshot(companyId, version: 4);
        await using var harness = await CreateHarnessAsync(initialSnapshot);
        var created = await harness.Service.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                initialSnapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "he",
            acceptLanguageHeader: "he",
            CancellationToken.None);

        var refreshedSnapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 5,
            branchId: initialSnapshot.Branches.Single().Id,
            categoryId: initialSnapshot.Categories.Single().Id,
            offeringId: initialSnapshot.Categories.Single().Offerings.Single().Id,
            addonGroupId: initialSnapshot.Categories.Single().Offerings.Single().AddonGroups.Single().Id,
            addonChoiceId: initialSnapshot.Categories.Single().Offerings.Single().AddonGroups.Single().Choices.Single().Id);
        await CheckoutDraftTestSupport.ApplySnapshotAsync(harness.Context, refreshedSnapshot);

        var updated = await harness.Service.UpdateAsync(
            created.OrderGuid,
            CheckoutDraftTestSupport.CreateValidUpdateRequest(
                refreshedSnapshot,
                new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
                expectedVersion: created.Version),
            harness.DeviceId,
            requestedLanguage: "he",
            acceptLanguageHeader: "he",
            CancellationToken.None);

        updated.Intent.CatalogVersion.Should().Be(5);
        (await harness.Context.CheckoutDrafts.Select(draft => draft.CatalogVersion).SingleAsync()).Should().Be(5);
    }

    [Fact]
    public async Task UpdateAsync_WhenItemsAndSelectionsAreReplaced_PersistsOnlyTheLatestGraph()
    {
        var snapshot = CreateTwoOfferingSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var firstOffering = snapshot.Categories.Single().Offerings.First();
        var secondOffering = snapshot.Categories.Single().Offerings.Last();
        var firstChoiceId = firstOffering.AddonGroups.Single().Choices.Single().Id;
        var secondChoiceId = secondOffering.AddonGroups.Single().Choices.Single().Id;
        var createRequest = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        createRequest.Items =
        [
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = firstOffering.Id,
                Selections =
                [
                    new CheckoutDraftSelectionRequest
                    {
                        AddonChoiceSourceId = firstChoiceId,
                        Quantity = 1
                    }
                ]
            }
        ];

        var created = await harness.Service.CreateAsync(
            createRequest,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        var updateRequest = CheckoutDraftTestSupport.CreateValidUpdateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
            expectedVersion: created.Version);
        updateRequest.Items =
        [
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = secondOffering.Id,
                Selections =
                [
                    new CheckoutDraftSelectionRequest
                    {
                        AddonChoiceSourceId = secondChoiceId,
                        Quantity = 1
                    }
                ]
            }
        ];

        var updated = await harness.Service.UpdateAsync(
            created.OrderGuid,
            updateRequest,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        updated.Version.Should().Be(2);
        updated.Intent.Items.Should().ContainSingle();
        updated.Intent.Items.Single().OfferingSourceId.Should().Be(secondOffering.Id);
        updated.Intent.Items.Single().Selections.Should().ContainSingle(selection =>
            selection.AddonChoiceSourceId == secondChoiceId &&
            selection.Quantity == 1);

        var storedDraft = await harness.Context.CheckoutDrafts
            .Include(draft => draft.Items)
                .ThenInclude(item => item.Selections)
            .SingleAsync();
        storedDraft.Items.Should().ContainSingle();
        storedDraft.Items.Single().OfferingSourceId.Should().Be(secondOffering.Id);
        storedDraft.Items.SelectMany(item => item.Selections)
            .Should()
            .ContainSingle(selection => selection.AddonChoiceSourceId == secondChoiceId && selection.Quantity == 1);
        storedDraft.Items.SelectMany(item => item.Selections)
            .Should()
            .NotContain(selection => selection.AddonChoiceSourceId == firstChoiceId);
    }

    [Fact]
    public async Task CreateAsync_WhenAddonBelongsToDifferentOffering_ThrowsSelectionInvalid()
    {
        var snapshot = CreateTwoOfferingSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var firstOffering = snapshot.Categories.Single().Offerings.First();
        var foreignChoiceId = snapshot.Categories.Single().Offerings.Last()
            .AddonGroups.Single()
            .Choices.Single()
            .Id;
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
                        AddonChoiceSourceId = foreignChoiceId,
                        Quantity = 1
                    }
                ]
            }
        ];

        var action = () => harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SelectionInvalid);
    }

    [Fact]
    public async Task CreateAsync_WhenBranchBelongsToDifferentProvider_ThrowsSelectionInvalid()
    {
        var firstSnapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 11);
        var secondSnapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 12);
        await using var harness = await CreateHarnessAsync(firstSnapshot);
        await CheckoutDraftTestSupport.SeedSnapshotAsync(harness.Context, secondSnapshot, displayOrder: 1);
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            firstSnapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        request.BranchSourceId = secondSnapshot.Branches.Single().Id;

        var action = () => harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SelectionInvalid);
    }

    [Fact]
    public async Task CreateAsync_WhenOfferingBelongsToDifferentBranch_ThrowsSelectionInvalid()
    {
        var snapshot = CreateTwoBranchSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        request.Items =
        [
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = snapshot.Categories.Single().Offerings.Last().Id
            }
        ];

        var action = () => harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SelectionInvalid);
    }

    [Fact]
    public async Task CreateAsync_WhenRequiredSingleChoiceHasExactlyOneSelection_Succeeds()
    {
        var snapshot = CreateSelectionRulesSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var offering = GetOffering(snapshot, SingleChoiceOfferingReferenceCode);
        var selectedChoiceId = offering.AddonGroups.Single().Choices.Last().Id;

        var response = await harness.Service.CreateAsync(
            CreateRequestForOffering(
                snapshot,
                SingleChoiceOfferingReferenceCode,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
                (selectedChoiceId, 1)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Intent.Items.Should().ContainSingle();
        response.Intent.Items.Single().Selections.Should().ContainSingle(selection =>
            selection.AddonChoiceSourceId == selectedChoiceId &&
            selection.Quantity == 1);
    }

    [Fact]
    public async Task CreateAsync_WhenRequiredSingleChoiceIsMissing_ThrowsSelectionInvalid()
    {
        var snapshot = CreateSelectionRulesSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);

        var action = () => harness.Service.CreateAsync(
            CreateRequestForOffering(
                snapshot,
                SingleChoiceOfferingReferenceCode,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SelectionInvalid);
    }

    [Fact]
    public async Task CreateAsync_WhenSingleChoiceIncludesMoreThanOneChoice_ThrowsSelectionInvalid()
    {
        var snapshot = CreateSelectionRulesSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var choices = GetOffering(snapshot, SingleChoiceOfferingReferenceCode)
            .AddonGroups.Single()
            .Choices
            .Select(choice => choice.Id)
            .ToArray();

        var action = () => harness.Service.CreateAsync(
            CreateRequestForOffering(
                snapshot,
                SingleChoiceOfferingReferenceCode,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
                (choices[0], 1),
                (choices[1], 1)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SelectionInvalid);
    }

    [Fact]
    public async Task CreateAsync_WhenQuantityCounterTotalIsWithinRange_Succeeds()
    {
        var snapshot = CreateSelectionRulesSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var choices = GetOffering(snapshot, QuantityCounterOfferingReferenceCode)
            .AddonGroups.Single()
            .Choices
            .Select(choice => choice.Id)
            .ToArray();

        var response = await harness.Service.CreateAsync(
            CreateRequestForOffering(
                snapshot,
                QuantityCounterOfferingReferenceCode,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
                (choices[0], 1),
                (choices[1], 2)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Intent.Items.Should().ContainSingle();
        response.Intent.Items.Single().Selections.Should().HaveCount(2);
        response.Intent.Items.Single().Selections.Sum(selection => selection.Quantity).Should().Be(3);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    public async Task CreateAsync_WhenQuantityCounterTotalFallsOutsideConfiguredRange_ThrowsSelectionInvalid(
        int firstChoiceQuantity,
        int secondChoiceQuantity)
    {
        var snapshot = CreateSelectionRulesSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var choices = GetOffering(snapshot, QuantityCounterOfferingReferenceCode)
            .AddonGroups.Single()
            .Choices
            .Select(choice => choice.Id)
            .ToArray();

        var action = () => harness.Service.CreateAsync(
            CreateRequestForOffering(
                snapshot,
                QuantityCounterOfferingReferenceCode,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
                (choices[0], firstChoiceQuantity),
                (choices[1], secondChoiceQuantity)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SelectionInvalid);
    }

    [Fact]
    public async Task CreateAsync_WhenFixedIncludedChoiceIsOmitted_AppliesCatalogDefaultSelection()
    {
        var snapshot = CreateSelectionRulesSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var fixedChoiceId = GetOffering(snapshot, FixedIncludedOfferingReferenceCode)
            .AddonGroups.Single()
            .Choices.Single()
            .Id;

        var response = await harness.Service.CreateAsync(
            CreateRequestForOffering(
                snapshot,
                FixedIncludedOfferingReferenceCode,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Intent.Items.Should().ContainSingle();
        response.Intent.Items.Single().Selections.Should().ContainSingle(selection =>
            selection.AddonChoiceSourceId == fixedChoiceId &&
            selection.Quantity == 1);
    }

    [Fact]
    public async Task CreateAsync_WhenFixedIncludedChoiceMatchesAuthoritativeDefault_Succeeds()
    {
        var snapshot = CreateSelectionRulesSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var fixedChoiceId = GetOffering(snapshot, FixedIncludedOfferingReferenceCode)
            .AddonGroups.Single()
            .Choices.Single()
            .Id;

        var response = await harness.Service.CreateAsync(
            CreateRequestForOffering(
                snapshot,
                FixedIncludedOfferingReferenceCode,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
                (fixedChoiceId, 1)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Intent.Items.Should().ContainSingle();
        response.Intent.Items.Single().Selections.Should().ContainSingle(selection =>
            selection.AddonChoiceSourceId == fixedChoiceId &&
            selection.Quantity == 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task CreateAsync_WhenFixedIncludedChoiceQuantityIsTampered_ThrowsSelectionInvalid(
        int quantity)
    {
        var snapshot = CreateSelectionRulesSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var fixedChoiceId = GetOffering(snapshot, FixedIncludedOfferingReferenceCode)
            .AddonGroups.Single()
            .Choices.Single()
            .Id;

        var action = () => harness.Service.CreateAsync(
            CreateRequestForOffering(
                snapshot,
                FixedIncludedOfferingReferenceCode,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
                (fixedChoiceId, quantity)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SelectionInvalid);
    }

    [Fact]
    public async Task CreateAsync_WhenFixedIncludedChoiceUsesForeignChoice_ThrowsSelectionInvalid()
    {
        var snapshot = CreateSelectionRulesSnapshot(Guid.NewGuid());
        await using var harness = await CreateHarnessAsync(snapshot);
        var foreignChoiceId = GetOffering(snapshot, SingleChoiceOfferingReferenceCode)
            .AddonGroups.Single()
            .Choices.First()
            .Id;

        var action = () => harness.Service.CreateAsync(
            CreateRequestForOffering(
                snapshot,
                FixedIncludedOfferingReferenceCode,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
                (foreignChoiceId, 1)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SelectionInvalid);
    }

    [Fact]
    public async Task CreateAsync_WhenItemElementIsNull_ThrowsStableInvalidProblem()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 5);
        await using var harness = await CreateHarnessAsync(snapshot);
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        request.Items = [null!];

        var action = () => harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception =>
                exception.Code == CheckoutDraftProblemCodes.Invalid &&
                exception.FieldErrors != null &&
                exception.FieldErrors.ContainsKey("items[0]"));
    }

    [Fact]
    public async Task CreateAsync_WhenSelectionElementIsNull_ThrowsStableInvalidProblem()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 5);
        await using var harness = await CreateHarnessAsync(snapshot);
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        request.Items =
        [
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = snapshot.Categories.Single().Offerings.Single().Id,
                Selections = [null!]
            }
        ];

        var action = () => harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception =>
                exception.Code == CheckoutDraftProblemCodes.Invalid &&
                exception.FieldErrors != null &&
                exception.FieldErrors.ContainsKey("items[0].selections[0]"));
    }

    [Fact]
    public async Task CreateAsync_WhenLegacyAvailabilityIsMissing_RefreshesProviderAndReloadsConsistentGraph()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var offeringId = Guid.NewGuid();
        var addonGroupId = Guid.NewGuid();
        var addonChoiceId = Guid.NewGuid();
        var legacySnapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 20,
            branchId: branchId,
            categoryId: categoryId,
            offeringId: offeringId,
            addonGroupId: addonGroupId,
            addonChoiceId: addonChoiceId,
            includeAvailability: false);
        var refreshedSnapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 21,
            branchId: branchId,
            categoryId: categoryId,
            offeringId: offeringId,
            addonGroupId: addonGroupId,
            addonChoiceId: addonChoiceId);
        await using var harness = await CreateHarnessAsync(legacySnapshot);
        harness.BusinessApiClient.GetCatalogSnapshotHandler = (_, _) => Task.FromResult(refreshedSnapshot);

        var response = await harness.Service.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                legacySnapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Intent.CatalogVersion.Should().Be(21);
        harness.BusinessApiClient.CatalogSnapshotRequests.Should().Be(1);
        (await harness.Context.CheckoutDrafts.Select(draft => draft.CatalogVersion).SingleAsync()).Should().Be(21);
        (await harness.Context.CatalogBranches.Select(branch => branch.AvailabilitySnapshotJson).SingleAsync())
            .Should()
            .NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateAsync_WhenLegacyAvailabilityRefreshFails_ThrowsSlotUnavailable()
    {
        var companyId = Guid.NewGuid();
        var legacySnapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 20,
            branchId: Guid.NewGuid(),
            categoryId: Guid.NewGuid(),
            offeringId: Guid.NewGuid(),
            addonGroupId: Guid.NewGuid(),
            addonChoiceId: Guid.NewGuid(),
            includeAvailability: false);
        await using var harness = await CreateHarnessAsync(legacySnapshot);
        harness.BusinessApiClient.GetCatalogSnapshotHandler = (_, _) =>
            throw new BusinessApiUnavailableException("Unavailable", "corr-step10-refresh");

        var action = () => harness.Service.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                legacySnapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SlotUnavailable);
        harness.BusinessApiClient.CatalogSnapshotRequests.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenRefreshedAvailabilityRemainsMissing_ThrowsSlotUnavailable()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var offeringId = Guid.NewGuid();
        var addonGroupId = Guid.NewGuid();
        var addonChoiceId = Guid.NewGuid();
        var legacySnapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 20,
            branchId: branchId,
            categoryId: categoryId,
            offeringId: offeringId,
            addonGroupId: addonGroupId,
            addonChoiceId: addonChoiceId,
            includeAvailability: false);
        var refreshedSnapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 21,
            branchId: branchId,
            categoryId: categoryId,
            offeringId: offeringId,
            addonGroupId: addonGroupId,
            addonChoiceId: addonChoiceId,
            includeAvailability: false);
        await using var harness = await CreateHarnessAsync(legacySnapshot);
        harness.BusinessApiClient.GetCatalogSnapshotHandler = (_, _) => Task.FromResult(refreshedSnapshot);

        var action = () => harness.Service.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                legacySnapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SlotUnavailable);
        harness.BusinessApiClient.CatalogSnapshotRequests.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenAvailabilityIsAlreadyPopulated_DoesNotRefreshProvider()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 22);
        await using var harness = await CreateHarnessAsync(snapshot);

        var response = await harness.Service.CreateAsync(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)),
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Intent.CatalogVersion.Should().Be(22);
        harness.BusinessApiClient.CatalogSnapshotRequests.Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WhenLocationIsOutsideServiceArea_ThrowsStableError()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 5);
        await using var harness = await CreateHarnessAsync(snapshot);
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        request.Location.Latitude = 35.5;
        request.Location.Longitude = 36.5;

        var action = () => harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.OutOfServiceArea);
    }

    [Fact]
    public async Task CreateAsync_WhenLocationIsExactlyOnServiceAreaBoundary_Succeeds()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 13);
        await using var harness = await CreateHarnessAsync(snapshot);
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        var location = CreateLocationAtServiceAreaBoundary(
            centerLatitude: 32.1,
            centerLongitude: 34.8,
            radiusKm: 12d);
        request.Location.Latitude = location.Latitude;
        request.Location.Longitude = location.Longitude;

        var response = await harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.OrderGuid.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateAsync_WhenRequestedSlotViolatesLeadTime_ThrowsSlotUnavailable()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 6);
        await using var harness = await CreateHarnessAsync(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 8, 20, 0, TimeSpan.Zero));
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 8, 30, 0, TimeSpan.Zero));

        var action = () => harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SlotUnavailable);
    }

    [Fact]
    public async Task CreateAsync_WhenRequestedSlotMatchesLeadBoundary_Succeeds()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 14);
        await using var harness = await CreateHarnessAsync(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 8, 30, 0, TimeSpan.Zero));

        var response = await harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        response.Intent.RequestedSlotStartUtc.Should().Be(request.RequestedSlotStartUtc.ToUniversalTime());
    }

    [Fact]
    public async Task CreateAsync_WhenRequestedSlotExceedsHorizon_ThrowsSlotUnavailable()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(
            Guid.NewGuid(),
            version: 15,
            availability: CreateAvailability(bookingHorizonDays: 1));
        await using var harness = await CreateHarnessAsync(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero));

        var action = () => harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SlotUnavailable);
    }

    [Fact]
    public async Task CreateAsync_WhenRequestedSlotIsMisaligned_ThrowsSlotUnavailable()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 16);
        await using var harness = await CreateHarnessAsync(snapshot);
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 15, 0, TimeSpan.Zero));

        var action = () => harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SlotUnavailable);
    }

    [Fact]
    public async Task CreateAsync_WhenRequestedSlotFallsIntoDstAmbiguity_ThrowsSlotUnavailable()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(
            Guid.NewGuid(),
            version: 17,
            availability: CreateAvailability(
                timeZoneId: "Pacific Standard Time",
                minimumLeadMinutes: 0,
                bookingHorizonDays: 7,
                recurringSchedules:
                [
                    new CatalogSnapshotRecurringSchedule
                    {
                        DayOfWeek = DayOfWeek.Sunday,
                        StartLocalTime = TimeSpan.Zero,
                        EndLocalTime = TimeSpan.FromHours(4),
                        SlotDurationMinutes = 30,
                        Capacity = 4
                    }
                ]));
        await using var harness = await CreateHarnessAsync(
            snapshot,
            new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero));
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 11, 1, 8, 30, 0, TimeSpan.Zero));

        var action = () => harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        await action.Should().ThrowAsync<CheckoutDraftException>()
            .Where(exception => exception.Code == CheckoutDraftProblemCodes.SlotUnavailable);
    }

    [Fact]
    public async Task CreateAsync_LogsWithoutSensitiveLocationOrLicensePlateValues()
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 8);
        await using var harness = await CreateHarnessAsync(snapshot);
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        request.Vehicle.LicensePlate = "PRIVATE-PLATE-7788";
        request.Location.AddressLine = "PRIVATE-ADDRESS-7788";

        await harness.Service.CreateAsync(
            request,
            harness.DeviceId,
            requestedLanguage: "ar",
            acceptLanguageHeader: "ar",
            CancellationToken.None);

        harness.Logger.InfoMessages.Should().NotContain(message => message.Contains("PRIVATE-PLATE-7788", StringComparison.Ordinal));
        harness.Logger.InfoMessages.Should().NotContain(message => message.Contains("PRIVATE-ADDRESS-7788", StringComparison.Ordinal));
        harness.Logger.WarningMessages.Should().NotContain(message => message.Contains("PRIVATE-PLATE-7788", StringComparison.Ordinal));
    }

    private static async Task<Harness> CreateHarnessAsync(
        CatalogSnapshotResponse snapshot,
        DateTimeOffset? utcNow = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        await CheckoutDraftTestSupport.SeedSnapshotAsync(context, snapshot);

        var logger = new TestAppLogger();
        var catalogLogger = new TestAppLogger();
        var timeProvider = new ManualTimeProvider(
            utcNow ?? new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        var businessApiClient = new ScriptedBusinessApiClient
        {
            GetCatalogSnapshotHandler = (companyId, _) => Task.FromResult(snapshot)
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
            catalogLogger);

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
                logger),
            timeProvider,
            logger,
            businessApiClient);
    }

    private static CatalogSnapshotResponse CreateTwoOfferingSnapshot(Guid companyId)
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(companyId, version: 9);
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

    private static CatalogSnapshotResponse CreateTwoBranchSnapshot(Guid companyId)
    {
        var firstBranchId = Guid.NewGuid();
        var secondBranchId = Guid.NewGuid();
        var snapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 18,
            branchId: firstBranchId,
            offeringId: Guid.NewGuid(),
            addonGroupId: Guid.NewGuid(),
            addonChoiceId: Guid.NewGuid());
        var firstBranch = snapshot.Branches.Single();
        var firstOffering = snapshot.Categories.Single().Offerings.Single();

        snapshot.Branches =
        [
            firstBranch,
            new CatalogSnapshotBranch
            {
                Id = secondBranchId,
                NameAr = "فرع ثانوي",
                NameHe = "סניף משני",
                AddressAr = "شارع فرعي 2",
                AddressHe = "רחוב צדדי 2",
                Latitude = 32.11,
                Longitude = 34.81,
                Availability = CreateAvailability()
            }
        ];
        snapshot.ServiceAreas =
        [
            snapshot.ServiceAreas.Single(),
            new CatalogSnapshotServiceArea
            {
                BranchId = secondBranchId,
                IsActive = true,
                UsesBranchCoordinates = true,
                CenterLatitude = 32.11,
                CenterLongitude = 34.81,
                RadiusKm = 12
            }
        ];
        snapshot.Categories =
        [
            new CatalogSnapshotCategory
            {
                Id = snapshot.Categories.Single().Id,
                NameAr = snapshot.Categories.Single().NameAr,
                NameHe = snapshot.Categories.Single().NameHe,
                DescriptionAr = snapshot.Categories.Single().DescriptionAr,
                DescriptionHe = snapshot.Categories.Single().DescriptionHe,
                DisplayOrder = snapshot.Categories.Single().DisplayOrder,
                Offerings =
                [
                    firstOffering,
                    new CatalogSnapshotOffering
                    {
                        Id = Guid.NewGuid(),
                        BranchId = secondBranchId,
                        NameAr = "غسيل الفرع الثاني",
                        NameHe = "שטיפה לסניף השני",
                        DescriptionAr = "خدمة خاصة بالفرع الثاني",
                        DescriptionHe = "שירות לסניף השני",
                        BasePrice = 85m,
                        DurationMinutes = 30,
                        ReferenceCode = "BRANCH-02",
                        DisplayOrder = 5,
                        AddonGroups = []
                    }
                ]
            }
        ];

        return snapshot;
    }

    private static CatalogSnapshotResponse CreateSelectionRulesSnapshot(Guid companyId)
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 19,
            offeringId: Guid.NewGuid(),
            addonGroupId: Guid.NewGuid(),
            addonChoiceId: Guid.NewGuid());
        var category = snapshot.Categories.Single();
        var branchId = snapshot.Branches.Single().Id;

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
                Offerings =
                [
                    CreateSingleChoiceOffering(branchId),
                    CreateQuantityCounterOffering(branchId),
                    CreateFixedIncludedOffering(branchId)
                ]
            }
        ];

        return snapshot;
    }

    private static CatalogSnapshotOffering GetOffering(
        CatalogSnapshotResponse snapshot,
        string referenceCode) =>
        snapshot.Categories.Single().Offerings.Single(offering => offering.ReferenceCode == referenceCode);

    private static CreateCheckoutDraftRequest CreateRequestForOffering(
        CatalogSnapshotResponse snapshot,
        string referenceCode,
        DateTimeOffset requestedSlotStartUtc,
        params (Guid AddonChoiceSourceId, int Quantity)[] selections)
    {
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(snapshot, requestedSlotStartUtc);
        var offering = GetOffering(snapshot, referenceCode);
        request.Items =
        [
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = offering.Id,
                Selections = selections
                    .Select(selection => new CheckoutDraftSelectionRequest
                    {
                        AddonChoiceSourceId = selection.AddonChoiceSourceId,
                        Quantity = selection.Quantity
                    })
                    .ToArray()
            }
        ];

        return request;
    }

    private static CatalogSnapshotOffering CreateSingleChoiceOffering(Guid branchId) =>
        new()
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            NameAr = "اختيار واحد",
            NameHe = "בחירה אחת",
            DescriptionAr = "خدمة باختيار واحد",
            DescriptionHe = "שירות עם בחירה אחת",
            BasePrice = 55m,
            DurationMinutes = 30,
            ReferenceCode = SingleChoiceOfferingReferenceCode,
            DisplayOrder = 10,
            AddonGroups =
            [
                new CatalogSnapshotAddonGroup
                {
                    Id = Guid.NewGuid(),
                    NameAr = "عطر السيارة",
                    NameHe = "ניחוח לרכב",
                    SelectionType = "SingleChoice",
                    IsRequired = true,
                    MinimumSelections = 1,
                    MaximumSelections = 1,
                    DisplayOrder = 1,
                    Choices =
                    [
                        new CatalogSnapshotAddonChoice
                        {
                            Id = Guid.NewGuid(),
                            NameAr = "ليمون",
                            NameHe = "לימון",
                            PriceAdjustment = 0m,
                            DurationAdjustmentMinutes = 0,
                            DefaultQuantity = 0,
                            DisplayOrder = 1
                        },
                        new CatalogSnapshotAddonChoice
                        {
                            Id = Guid.NewGuid(),
                            NameAr = "فانيلا",
                            NameHe = "וניל",
                            PriceAdjustment = 2m,
                            DurationAdjustmentMinutes = 0,
                            DefaultQuantity = 0,
                            DisplayOrder = 2
                        }
                    ]
                }
            ]
        };

    private static CatalogSnapshotOffering CreateQuantityCounterOffering(Guid branchId) =>
        new()
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            NameAr = "عداد الكمية",
            NameHe = "מונה כמות",
            DescriptionAr = "خدمة بعدّاد كمية",
            DescriptionHe = "שירות עם מונה כמות",
            BasePrice = 65m,
            DurationMinutes = 30,
            ReferenceCode = QuantityCounterOfferingReferenceCode,
            DisplayOrder = 11,
            AddonGroups =
            [
                new CatalogSnapshotAddonGroup
                {
                    Id = Guid.NewGuid(),
                    NameAr = "مناشف",
                    NameHe = "מגבות",
                    SelectionType = "QuantityCounter",
                    IsRequired = true,
                    MinimumSelections = 2,
                    MaximumSelections = 3,
                    DisplayOrder = 1,
                    Choices =
                    [
                        new CatalogSnapshotAddonChoice
                        {
                            Id = Guid.NewGuid(),
                            NameAr = "منشفة صغيرة",
                            NameHe = "מגבת קטנה",
                            PriceAdjustment = 1m,
                            DurationAdjustmentMinutes = 0,
                            DefaultQuantity = 0,
                            DisplayOrder = 1
                        },
                        new CatalogSnapshotAddonChoice
                        {
                            Id = Guid.NewGuid(),
                            NameAr = "منشفة كبيرة",
                            NameHe = "מגבת גדולה",
                            PriceAdjustment = 2m,
                            DurationAdjustmentMinutes = 0,
                            DefaultQuantity = 0,
                            DisplayOrder = 2
                        }
                    ]
                }
            ]
        };

    private static CatalogSnapshotOffering CreateFixedIncludedOffering(Guid branchId) =>
        new()
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            NameAr = "خيار ثابت",
            NameHe = "בחירה קבועה",
            DescriptionAr = "خدمة بخيار ثابت",
            DescriptionHe = "שירות עם בחירה קבועה",
            BasePrice = 45m,
            DurationMinutes = 30,
            ReferenceCode = FixedIncludedOfferingReferenceCode,
            DisplayOrder = 12,
            AddonGroups =
            [
                new CatalogSnapshotAddonGroup
                {
                    Id = Guid.NewGuid(),
                    NameAr = "تضمين افتراضي",
                    NameHe = "כלול כברירת מחדל",
                    SelectionType = "FixedIncludedChoice",
                    IsRequired = true,
                    MinimumSelections = 1,
                    MaximumSelections = 1,
                    DisplayOrder = 1,
                    Choices =
                    [
                        new CatalogSnapshotAddonChoice
                        {
                            Id = Guid.NewGuid(),
                            NameAr = "مشمّل",
                            NameHe = "כלול",
                            PriceAdjustment = 0m,
                            DurationAdjustmentMinutes = 0,
                            DefaultQuantity = 1,
                            DisplayOrder = 1
                        }
                    ]
                }
            ]
        };

    private static CatalogSnapshotBranchAvailability CreateAvailability(
        string timeZoneId = "UTC",
        int minimumLeadMinutes = 30,
        int bookingHorizonDays = 30,
        IReadOnlyCollection<CatalogSnapshotRecurringSchedule>? recurringSchedules = null,
        IReadOnlyCollection<CatalogSnapshotAvailabilityOverride>? availabilityOverrides = null) =>
        new()
        {
            IsActive = true,
            TimeZoneId = timeZoneId,
            MinimumLeadMinutes = minimumLeadMinutes,
            BookingHorizonDays = bookingHorizonDays,
            RecurringSchedules = recurringSchedules ?? CreateDefaultSchedules(),
            AvailabilityOverrides = availabilityOverrides ?? Array.Empty<CatalogSnapshotAvailabilityOverride>()
        };

    private static IReadOnlyCollection<CatalogSnapshotRecurringSchedule> CreateDefaultSchedules() =>
        Enum.GetValues<DayOfWeek>()
            .Select(dayOfWeek => new CatalogSnapshotRecurringSchedule
            {
                DayOfWeek = dayOfWeek,
                StartLocalTime = TimeSpan.FromHours(8),
                EndLocalTime = TimeSpan.FromHours(18),
                SlotDurationMinutes = 30,
                Capacity = 4
            })
            .ToArray();

    private static (double Latitude, double Longitude) CreateLocationAtServiceAreaBoundary(
        double centerLatitude,
        double centerLongitude,
        double radiusKm)
    {
        const double earthRadiusKm = 6371.0088d;
        var lowerBound = 0d;
        var upperBound = radiusKm / earthRadiusKm / Math.Max(Math.Cos(DegreesToRadians(centerLatitude)), 0.000001d);

        for (var iteration = 0; iteration < 80; iteration++)
        {
            var candidate = (lowerBound + upperBound) / 2d;
            var candidateLongitude = centerLongitude + (candidate * 180d / Math.PI);
            var distance = CalculateDistanceKm(centerLatitude, centerLongitude, centerLatitude, candidateLongitude);
            if (distance <= radiusKm)
            {
                lowerBound = candidate;
            }
            else
            {
                upperBound = candidate;
            }
        }

        return (
            centerLatitude,
            centerLongitude + (lowerBound * 180d / Math.PI));
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);

    private static double CalculateDistanceKm(
        double startLatitude,
        double startLongitude,
        double endLatitude,
        double endLongitude)
    {
        const double earthRadiusKm = 6371.0088d;
        var deltaLatitude = DegreesToRadians(endLatitude - startLatitude);
        var deltaLongitude = DegreesToRadians(endLongitude - startLongitude);
        var startLatitudeRadians = DegreesToRadians(startLatitude);
        var endLatitudeRadians = DegreesToRadians(endLatitude);

        var haversine = Math.Pow(Math.Sin(deltaLatitude / 2d), 2d)
            + Math.Cos(startLatitudeRadians)
            * Math.Cos(endLatitudeRadians)
            * Math.Pow(Math.Sin(deltaLongitude / 2d), 2d);

        var centralAngle = 2d * Math.Asin(Math.Min(1d, Math.Sqrt(haversine)));
        return earthRadiusKm * centralAngle;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(
            ApplicationDbContext context,
            CheckoutDraftService service,
            ManualTimeProvider timeProvider,
            TestAppLogger logger,
            ScriptedBusinessApiClient businessApiClient)
        {
            Context = context;
            Service = service;
            TimeProvider = timeProvider;
            Logger = logger;
            BusinessApiClient = businessApiClient;
        }

        public ApplicationDbContext Context { get; }
        public CheckoutDraftService Service { get; }
        public ManualTimeProvider TimeProvider { get; }
        public TestAppLogger Logger { get; }
        public ScriptedBusinessApiClient BusinessApiClient { get; }
        public Guid DeviceId { get; } = Guid.NewGuid();

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
