using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace GhseeliApis.Services.Checkout;

public interface ICheckoutDraftService
{
    Task<CheckoutDraftResponse> CreateAsync(
        CreateCheckoutDraftRequest request,
        Guid deviceId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);

    Task<CheckoutDraftResponse> GetAsync(
        Guid orderGuid,
        Guid deviceId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);

    Task<CheckoutDraftResponse> UpdateAsync(
        Guid orderGuid,
        UpdateCheckoutDraftRequest request,
        Guid deviceId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken);
}

public sealed class CheckoutDraftException : Exception
{
    public CheckoutDraftException(
        string code,
        int statusCode,
        string message,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        FieldErrors = fieldErrors;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }
}

public sealed class CheckoutDraftService : ICheckoutDraftService
{
    private const int MaximumGroupsPerItem = 10;
    private const int MaximumSelectionsPerItem = 25;
    private const int MaximumTotalDurationMinutes = 1_440;
    private const double EarthRadiusKm = 6371.0088d;
    private static readonly JsonSerializerOptions JsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    private readonly ICheckoutDraftRepository _draftRepository;
    private readonly ICatalogReadModelRepository _catalogRepository;
    private readonly ICatalogProviderRefreshCoordinator _catalogRefreshCoordinator;
    private readonly TimeProvider _timeProvider;
    private readonly CheckoutDraftOptions _options;
    private readonly IAppLogger _logger;

    public CheckoutDraftService(
        ICheckoutDraftRepository draftRepository,
        ICatalogReadModelRepository catalogRepository,
        ICatalogProviderRefreshCoordinator catalogRefreshCoordinator,
        TimeProvider timeProvider,
        IOptions<CheckoutDraftOptions> options,
        IAppLogger logger)
    {
        _draftRepository = draftRepository;
        _catalogRepository = catalogRepository;
        _catalogRefreshCoordinator = catalogRefreshCoordinator;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CheckoutDraftResponse> CreateAsync(
        CreateCheckoutDraftRequest request,
        Guid deviceId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ConfigurationLanguageResolver.Resolve(requestedLanguage, acceptLanguageHeader);
        var now = _timeProvider.GetUtcNow();
        var normalized = await ValidateAndNormalizeAsync(request, cancellationToken);

        var draft = new CheckoutDraft
        {
            Id = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            OwnerDeviceId = deviceId,
            BusinessSourceId = normalized.BusinessSourceId,
            BranchSourceId = normalized.BranchSourceId,
            CatalogVersion = normalized.CatalogVersion,
            PublicVersion = 1,
            RequestedSlotStartUtc = normalized.RequestedSlotStartUtc,
            VehicleType = normalized.VehicleType,
            LicensePlate = normalized.LicensePlate,
            VehicleMake = normalized.VehicleMake,
            VehicleModel = normalized.VehicleModel,
            VehicleColor = normalized.VehicleColor,
            AddressLine = normalized.AddressLine,
            City = normalized.City,
            Area = normalized.Area,
            Latitude = ToCoordinate(normalized.Latitude),
            Longitude = ToCoordinate(normalized.Longitude),
            RequiresReprice = true,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddMinutes(_options.LifetimeMinutes),
            Items = CreateDraftItems(normalized.Items)
        };

        await _draftRepository.AddAsync(draft, cancellationToken);
        await _draftRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInfo(
            $"Created checkout draft {draft.OrderGuid:D} for device {deviceId:D} with {draft.Items.Count} item(s).");

        return MapResponse(draft, language);
    }

    public async Task<CheckoutDraftResponse> GetAsync(
        Guid orderGuid,
        Guid deviceId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ConfigurationLanguageResolver.Resolve(requestedLanguage, acceptLanguageHeader);
        var draft = await _draftRepository.GetOwnedByOrderGuidAsync(
            orderGuid,
            deviceId,
            cancellationToken);
        if (draft is null)
        {
            throw CreateNotFound();
        }

        EnsureActive(draft);
        return MapResponse(draft, language);
    }

    public async Task<CheckoutDraftResponse> UpdateAsync(
        Guid orderGuid,
        UpdateCheckoutDraftRequest request,
        Guid deviceId,
        string? requestedLanguage,
        string? acceptLanguageHeader,
        CancellationToken cancellationToken)
    {
        var language = ConfigurationLanguageResolver.Resolve(requestedLanguage, acceptLanguageHeader);
        var draft = await _draftRepository.GetOwnedByOrderGuidAsync(
            orderGuid,
            deviceId,
            cancellationToken);
        if (draft is null)
        {
            throw CreateNotFound();
        }

        EnsureActive(draft);
        if (draft.PublicVersion != request.ExpectedVersion)
        {
            throw CreateVersionConflict();
        }

        var now = _timeProvider.GetUtcNow();
        var normalized = await ValidateAndNormalizeAsync(request, cancellationToken);

        draft.BusinessSourceId = normalized.BusinessSourceId;
        draft.BranchSourceId = normalized.BranchSourceId;
        draft.CatalogVersion = normalized.CatalogVersion;
        draft.RequestedSlotStartUtc = normalized.RequestedSlotStartUtc;
        draft.VehicleType = normalized.VehicleType;
        draft.LicensePlate = normalized.LicensePlate;
        draft.VehicleMake = normalized.VehicleMake;
        draft.VehicleModel = normalized.VehicleModel;
        draft.VehicleColor = normalized.VehicleColor;
        draft.AddressLine = normalized.AddressLine;
        draft.City = normalized.City;
        draft.Area = normalized.Area;
        draft.Latitude = ToCoordinate(normalized.Latitude);
        draft.Longitude = ToCoordinate(normalized.Longitude);
        draft.RequiresReprice = true;
        draft.PricingSnapshot = null;
        draft.UpdatedAt = now;
        draft.ExpiresAt = now.AddMinutes(_options.LifetimeMinutes);
        draft.PublicVersion = checked(draft.PublicVersion + 1);
        SynchronizeItems(draft, normalized.Items);

        try
        {
            await _draftRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw CreateVersionConflict();
        }

        _logger.LogInfo(
            $"Updated checkout draft {draft.OrderGuid:D} for device {deviceId:D} to version {draft.PublicVersion}.");

        return MapResponse(draft, language);
    }

    private async Task<NormalizedDraftIntent> ValidateAndNormalizeAsync(
        CheckoutDraftMutationRequestBase request,
        CancellationToken cancellationToken)
    {
        ValidateRequestStructure(request);

        var provider = await _catalogRepository.GetEnabledProviderBySourceCompanyIdWithGraphAsync(
            request.BusinessSourceId,
            cancellationToken);
        if (provider is null)
        {
            throw CreateSelectionInvalid("businessSourceId");
        }

        var (branch, eligibleBranchIds) = ResolveEligibleBranch(provider, request.BranchSourceId);
        if (branch is null)
        {
            throw CreateSelectionInvalid("branchSourceId");
        }

        if (RequiresAvailabilityRefresh(branch))
        {
            try
            {
                await _catalogRefreshCoordinator.EnsureProviderUsableAsync(
                    provider,
                    forceRefresh: true,
                    cancellationToken);
            }
            catch (CatalogReadModelException)
            {
                throw CreateSlotUnavailable();
            }

            provider = await _catalogRepository.GetEnabledProviderBySourceCompanyIdWithGraphAsync(
                request.BusinessSourceId,
                cancellationToken);
            if (provider is null)
            {
                throw CreateSelectionInvalid("businessSourceId");
            }

            (branch, eligibleBranchIds) = ResolveEligibleBranch(provider, request.BranchSourceId);
            if (branch is null)
            {
                throw CreateSelectionInvalid("branchSourceId");
            }
        }

        var offeringsBySourceId = provider.Categories
            .SelectMany(category => category.Offerings)
            .Where(offering => !offering.BranchId.HasValue || eligibleBranchIds.Contains(offering.BranchId.Value))
            .ToDictionary(offering => offering.SourceOfferingId);

        var normalizedItems = new List<NormalizedDraftItem>();
        var totalDurationMinutes = 0;
        var requestedItems = request.Items.ToArray();
        for (var index = 0; index < requestedItems.Length; index++)
        {
            var requestedItem = requestedItems[index];
            if (requestedItem is null)
            {
                throw CreateInvalidRequest($"items[{index}]", CheckoutDraftFieldErrorCodes.Required);
            }

            if (!offeringsBySourceId.TryGetValue(requestedItem.OfferingSourceId, out var offering) ||
                (offering.BranchId.HasValue && offering.BranchId.Value != branch.Id))
            {
                throw CreateSelectionInvalid($"items[{index}].offeringSourceId");
            }

            var normalizedSelections = NormalizeSelections(offering, requestedItem, index, out var durationAdjustment);
            totalDurationMinutes = checked(totalDurationMinutes + offering.DurationMinutes + durationAdjustment);
            if (totalDurationMinutes > MaximumTotalDurationMinutes)
            {
                throw CreateSelectionInvalid("items");
            }

            normalizedItems.Add(new NormalizedDraftItem(
                requestedItem.OfferingSourceId,
                normalizedSelections));
        }

        ValidateLocation(branch, request.Location);
        ValidateSlot(branch, request.RequestedSlotStartUtc.ToUniversalTime(), totalDurationMinutes);

        return new NormalizedDraftIntent(
            request.BusinessSourceId,
            request.BranchSourceId,
            provider.CatalogVersion,
            request.RequestedSlotStartUtc.ToUniversalTime(),
            ConfigurationTextNormalizer.NormalizeRequired(request.Vehicle.VehicleType!),
            ConfigurationTextNormalizer.NormalizeOptional(request.Vehicle.LicensePlate),
            ConfigurationTextNormalizer.NormalizeOptional(request.Vehicle.Make),
            ConfigurationTextNormalizer.NormalizeOptional(request.Vehicle.Model),
            ConfigurationTextNormalizer.NormalizeOptional(request.Vehicle.Color),
            ConfigurationTextNormalizer.NormalizeRequired(request.Location.AddressLine!),
            ConfigurationTextNormalizer.NormalizeOptional(request.Location.City),
            ConfigurationTextNormalizer.NormalizeOptional(request.Location.Area),
            request.Location.Latitude,
            request.Location.Longitude,
            normalizedItems);
    }

    private static List<CheckoutDraftItem> CreateDraftItems(IReadOnlyList<NormalizedDraftItem> normalizedItems)
    {
        var items = new List<CheckoutDraftItem>(normalizedItems.Count);
        for (var itemIndex = 0; itemIndex < normalizedItems.Count; itemIndex++)
        {
            var normalizedItem = normalizedItems[itemIndex];
            var item = new CheckoutDraftItem
            {
                Id = Guid.NewGuid(),
                OfferingSourceId = normalizedItem.OfferingSourceId,
                DisplayOrder = itemIndex
            };

            var selections = new List<CheckoutDraftSelection>(normalizedItem.Selections.Count);
            for (var selectionIndex = 0; selectionIndex < normalizedItem.Selections.Count; selectionIndex++)
            {
                var normalizedSelection = normalizedItem.Selections[selectionIndex];
                selections.Add(new CheckoutDraftSelection
                {
                    Id = Guid.NewGuid(),
                    AddonGroupSourceId = normalizedSelection.AddonGroupSourceId,
                    AddonChoiceSourceId = normalizedSelection.AddonChoiceSourceId,
                    Quantity = normalizedSelection.Quantity,
                    DisplayOrder = selectionIndex
                });
            }

            item.Selections = selections;
            items.Add(item);
        }

        return items;
    }

    private static void SynchronizeItems(CheckoutDraft draft, IReadOnlyList<NormalizedDraftItem> normalizedItems)
    {
        var existingItemsByOffering = draft.Items.ToDictionary(item => item.OfferingSourceId);
        var normalizedOfferings = normalizedItems
            .Select(item => item.OfferingSourceId)
            .ToHashSet();
        var reusableItems = new Queue<CheckoutDraftItem>(draft.Items
            .Where(item => !normalizedOfferings.Contains(item.OfferingSourceId))
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Id));
        var synchronizedItems = new HashSet<CheckoutDraftItem>();

        for (var itemIndex = 0; itemIndex < normalizedItems.Count; itemIndex++)
        {
            var normalizedItem = normalizedItems[itemIndex];
            if (!existingItemsByOffering.TryGetValue(normalizedItem.OfferingSourceId, out var draftItem))
            {
                if (reusableItems.Count > 0)
                {
                    draftItem = reusableItems.Dequeue();
                }
                else
                {
                    draftItem = new CheckoutDraftItem
                    {
                        Id = Guid.NewGuid()
                    };
                    draft.Items.Add(draftItem);
                }
            }

            draftItem.OfferingSourceId = normalizedItem.OfferingSourceId;
            draftItem.DisplayOrder = itemIndex;
            SynchronizeSelections(draftItem, normalizedItem.Selections);
            synchronizedItems.Add(draftItem);
        }

        foreach (var itemToRemove in draft.Items
                     .Where(item => !synchronizedItems.Contains(item))
                     .ToArray())
        {
            foreach (var selectionToRemove in itemToRemove.Selections.ToArray())
            {
                itemToRemove.Selections.Remove(selectionToRemove);
            }

            draft.Items.Remove(itemToRemove);
        }
    }

    private static void SynchronizeSelections(
        CheckoutDraftItem draftItem,
        IReadOnlyList<NormalizedDraftSelection> normalizedSelections)
    {
        var existingSelectionsByChoice = draftItem.Selections.ToDictionary(
            selection => selection.AddonChoiceSourceId);
        var normalizedChoices = normalizedSelections
            .Select(selection => selection.AddonChoiceSourceId)
            .ToHashSet();
        var reusableSelections = new Queue<CheckoutDraftSelection>(draftItem.Selections
            .Where(selection => !normalizedChoices.Contains(selection.AddonChoiceSourceId))
            .OrderBy(selection => selection.DisplayOrder)
            .ThenBy(selection => selection.Id));
        var synchronizedSelections = new HashSet<CheckoutDraftSelection>();

        for (var selectionIndex = 0; selectionIndex < normalizedSelections.Count; selectionIndex++)
        {
            var normalizedSelection = normalizedSelections[selectionIndex];
            if (!existingSelectionsByChoice.TryGetValue(
                    normalizedSelection.AddonChoiceSourceId,
                    out var draftSelection))
            {
                if (reusableSelections.Count > 0)
                {
                    draftSelection = reusableSelections.Dequeue();
                }
                else
                {
                    draftSelection = new CheckoutDraftSelection
                    {
                        Id = Guid.NewGuid()
                    };
                    draftItem.Selections.Add(draftSelection);
                }
            }

            draftSelection.AddonGroupSourceId = normalizedSelection.AddonGroupSourceId;
            draftSelection.AddonChoiceSourceId = normalizedSelection.AddonChoiceSourceId;
            draftSelection.Quantity = normalizedSelection.Quantity;
            draftSelection.DisplayOrder = selectionIndex;
            synchronizedSelections.Add(draftSelection);
        }

        foreach (var selectionToRemove in draftItem.Selections
                     .Where(selection => !synchronizedSelections.Contains(selection))
                     .ToArray())
        {
            draftItem.Selections.Remove(selectionToRemove);
        }
    }

    private static IReadOnlyList<NormalizedDraftSelection> NormalizeSelections(
        CatalogOfferingReadModel offering,
        CheckoutDraftItemRequest requestItem,
        int itemIndex,
        out int durationAdjustmentMinutes)
    {
        if (requestItem.Selections is null)
        {
            throw CreateInvalidRequest(
                $"items[{itemIndex}].selections",
                CheckoutDraftFieldErrorCodes.CollectionRequired);
        }

        if (requestItem.Selections.Count > MaximumSelectionsPerItem)
        {
            throw CreateSelectionInvalid($"items[{itemIndex}].selections");
        }

        var choiceLookup = offering.AddonGroups
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.NameAr)
            .SelectMany(group => group.Choices.Select(choice => new ChoiceContext(group, choice)))
            .ToDictionary(context => context.Choice.SourceAddonChoiceId);

        var effectiveSelections = new Dictionary<Guid, (ChoiceContext Context, int Quantity)>();
        foreach (var choiceContext in choiceLookup.Values.Where(context => context.Choice.DefaultQuantity > 0))
        {
            effectiveSelections[choiceContext.Choice.SourceAddonChoiceId] =
                (choiceContext, choiceContext.Choice.DefaultQuantity);
        }

        var requestedSelections = requestItem.Selections.ToArray();
        for (var selectionIndex = 0; selectionIndex < requestedSelections.Length; selectionIndex++)
        {
            var selection = requestedSelections[selectionIndex];
            if (selection is null)
            {
                throw CreateInvalidRequest(
                    $"items[{itemIndex}].selections[{selectionIndex}]",
                    CheckoutDraftFieldErrorCodes.Required);
            }

            if (!choiceLookup.TryGetValue(selection.AddonChoiceSourceId, out var choiceContext))
            {
                throw CreateSelectionInvalid($"items[{itemIndex}].selections");
            }

            if (NormalizeSelectionType(choiceContext.Group.SelectionType) == CatalogSelectionType.FixedIncludedChoice &&
                (choiceContext.Choice.DefaultQuantity <= 0 ||
                 selection.Quantity != choiceContext.Choice.DefaultQuantity))
            {
                throw CreateSelectionInvalid($"items[{itemIndex}].selections");
            }

            effectiveSelections[selection.AddonChoiceSourceId] = (choiceContext, selection.Quantity);
        }

        var selectedGroupCount = effectiveSelections.Values
            .Where(value => value.Quantity > 0)
            .Select(value => value.Context.Group.SourceAddonGroupId)
            .Distinct()
            .Count();
        if (selectedGroupCount > MaximumGroupsPerItem)
        {
            throw new CheckoutDraftException(
                CheckoutDraftProblemCodes.Invalid,
                StatusCodes.Status400BadRequest,
                "Too many add-on groups were selected.",
                SingleFieldError(
                    $"items[{itemIndex}].selections",
                    CheckoutDraftFieldErrorCodes.CollectionTooMany));
        }

        var normalizedSelections = new List<NormalizedDraftSelection>();
        durationAdjustmentMinutes = 0;

        foreach (var group in offering.AddonGroups
                     .OrderBy(item => item.DisplayOrder)
                     .ThenBy(item => item.NameAr))
        {
            var selectionsForGroup = group.Choices
                .OrderBy(choice => choice.DisplayOrder)
                .ThenBy(choice => choice.NameAr)
                .Select(choice =>
                {
                    effectiveSelections.TryGetValue(choice.SourceAddonChoiceId, out var value);
                    return new GroupSelectionState(choice, value.Quantity);
                })
                .Where(state => state.Quantity > 0)
                .ToArray();

            if (!ValidateGroupSelection(group, selectionsForGroup))
            {
                throw CreateSelectionInvalid($"items[{itemIndex}].selections");
            }

            foreach (var state in selectionsForGroup)
            {
                normalizedSelections.Add(new NormalizedDraftSelection(
                    group.SourceAddonGroupId,
                    state.Choice.SourceAddonChoiceId,
                    state.Quantity));

                durationAdjustmentMinutes = checked(
                    durationAdjustmentMinutes + (state.Choice.DurationAdjustmentMinutes * state.Quantity));
            }
        }

        return normalizedSelections;
    }

    private static bool ValidateGroupSelection(
        CatalogAddonGroupReadModel group,
        IReadOnlyCollection<GroupSelectionState> selectedChoices)
    {
        var selectedCount = selectedChoices.Count;
        var quantitySum = selectedChoices.Sum(item => item.Quantity);

        return NormalizeSelectionType(group.SelectionType) switch
        {
            CatalogSelectionType.SingleChoice or CatalogSelectionType.SegmentedSingleButtonChoice =>
                !selectedChoices.Any(item => item.Quantity > 1) &&
                selectedCount >= group.MinimumSelections &&
                (!group.MaximumSelections.HasValue || selectedCount <= group.MaximumSelections.Value),
            CatalogSelectionType.MultipleChoice =>
                !selectedChoices.Any(item => item.Quantity > 1) &&
                selectedCount >= group.MinimumSelections &&
                (!group.MaximumSelections.HasValue || selectedCount <= group.MaximumSelections.Value),
            CatalogSelectionType.QuantityCounter =>
                quantitySum >= group.MinimumSelections &&
                (!group.MaximumSelections.HasValue || quantitySum <= group.MaximumSelections.Value),
            CatalogSelectionType.FixedIncludedChoice =>
                selectedCount == 1 && quantitySum == 1,
            _ => false
        };
    }

    private void ValidateLocation(
        CatalogBranchReadModel branch,
        CheckoutDraftLocationRequest location)
    {
        if (!branch.ServiceAreaRadiusKm.HasValue)
        {
            throw CreateSelectionInvalid("branchSourceId");
        }

        var (centerLatitude, centerLongitude) = ResolveServiceAreaCenter(branch);
        if (!centerLatitude.HasValue || !centerLongitude.HasValue)
        {
            throw CreateSelectionInvalid("branchSourceId");
        }

        var distanceKm = CalculateDistanceKm(
            centerLatitude.Value,
            centerLongitude.Value,
            location.Latitude,
            location.Longitude);

        if (distanceKm > branch.ServiceAreaRadiusKm.Value)
        {
            throw new CheckoutDraftException(
                CheckoutDraftProblemCodes.OutOfServiceArea,
                StatusCodes.Status400BadRequest,
                "The requested service location is outside the service area.",
                SingleFieldError("location", CheckoutDraftProblemCodes.OutOfServiceArea));
        }
    }

    private void ValidateSlot(
        CatalogBranchReadModel branch,
        DateTimeOffset requestedSlotStartUtc,
        int totalDurationMinutes)
    {
        var availability = DeserializeAvailability(branch);
        if (availability is null ||
            !availability.IsActive ||
            string.IsNullOrWhiteSpace(availability.TimeZoneId) ||
            totalDurationMinutes <= 0)
        {
            throw CreateSlotUnavailable();
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(availability.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw CreateSlotUnavailable();
        }
        catch (InvalidTimeZoneException)
        {
            throw CreateSlotUnavailable();
        }

        var normalizedStartUtc = requestedSlotStartUtc.UtcDateTime;
        DateTime normalizedEndUtc;
        try
        {
            normalizedEndUtc = normalizedStartUtc.AddMinutes(totalDurationMinutes);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw CreateSlotUnavailable();
        }

        var localStart = AsUnspecified(TimeZoneInfo.ConvertTimeFromUtc(normalizedStartUtc, timeZone));
        var localEnd = AsUnspecified(TimeZoneInfo.ConvertTimeFromUtc(normalizedEndUtc, timeZone));
        var actualDuration = normalizedEndUtc - normalizedStartUtc;

        if (timeZone.IsAmbiguousTime(localStart) ||
            timeZone.IsAmbiguousTime(localEnd) ||
            localEnd - localStart != actualDuration)
        {
            throw CreateSlotUnavailable();
        }

        var earliestStartUtc = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(availability.MinimumLeadMinutes);
        if (normalizedStartUtc < earliestStartUtc)
        {
            throw CreateSlotUnavailable();
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(_timeProvider.GetUtcNow().UtcDateTime, timeZone);
        if (localStart.Date > localNow.Date.AddDays(availability.BookingHorizonDays))
        {
            throw CreateSlotUnavailable();
        }

        var localStartDate = DateOnly.FromDateTime(localStart);
        var overrideForDate = availability.AvailabilityOverrides
            .SingleOrDefault(item => item.OverrideDate == localStartDate);
        IReadOnlyCollection<WindowOccurrence> windows = overrideForDate is not null
            ? GetWindowsFromOverride(localStartDate, overrideForDate)
            : GetWindowsForDate(availability, localStartDate)
                .Concat(GetCarryOverWindows(availability, localStartDate.AddDays(-1)))
                .OrderBy(window => window.StartLocalDateTime)
                .ThenBy(window => window.EndLocalDateTime)
                .ToArray();

        if (windows.Count == 0)
        {
            throw CreateSlotUnavailable();
        }

        var misaligned = false;
        foreach (var window in windows)
        {
            if (localStart < window.StartLocalDateTime ||
                localEnd > window.EndLocalDateTime)
            {
                continue;
            }

            var slotDuration = TimeSpan.FromMinutes(window.SlotDurationMinutes);
            var startOffset = localStart - window.StartLocalDateTime;
            var appointmentDuration = localEnd - localStart;
            if (appointmentDuration.Ticks % slotDuration.Ticks != 0 ||
                startOffset.Ticks % slotDuration.Ticks != 0)
            {
                misaligned = true;
                continue;
            }

            return;
        }

        if (misaligned)
        {
            throw CreateSlotUnavailable();
        }

        throw CreateSlotUnavailable();
    }

    private static CatalogSnapshotBranchAvailability? DeserializeAvailability(CatalogBranchReadModel branch)
    {
        if (string.IsNullOrWhiteSpace(branch.AvailabilitySnapshotJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CatalogSnapshotBranchAvailability>(
                branch.AvailabilitySnapshotJson,
                JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyCollection<WindowOccurrence> GetWindowsFromOverride(
        DateOnly date,
        CatalogSnapshotAvailabilityOverride availabilityOverride)
    {
        if (availabilityOverride.IsClosed ||
            !availabilityOverride.StartLocalTime.HasValue ||
            !availabilityOverride.EndLocalTime.HasValue ||
            !availabilityOverride.SlotDurationMinutes.HasValue)
        {
            return Array.Empty<WindowOccurrence>();
        }

        return
        [
            CreateOccurrence(
                date,
                availabilityOverride.StartLocalTime.Value,
                availabilityOverride.EndLocalTime.Value,
                availabilityOverride.SlotDurationMinutes.Value)
        ];
    }

    private static IReadOnlyCollection<WindowOccurrence> GetWindowsForDate(
        CatalogSnapshotBranchAvailability availability,
        DateOnly date)
    {
        var overrideForDate = availability.AvailabilityOverrides
            .SingleOrDefault(item => item.OverrideDate == date);
        if (overrideForDate is not null)
        {
            return GetWindowsFromOverride(date, overrideForDate);
        }

        return availability.RecurringSchedules
            .Where(schedule => schedule.DayOfWeek == date.DayOfWeek)
            .OrderBy(schedule => schedule.StartLocalTime)
            .Select(schedule => CreateOccurrence(
                date,
                schedule.StartLocalTime,
                schedule.EndLocalTime,
                schedule.SlotDurationMinutes))
            .ToArray();
    }

    private static IReadOnlyCollection<WindowOccurrence> GetCarryOverWindows(
        CatalogSnapshotBranchAvailability availability,
        DateOnly date)
    {
        return GetWindowsForDate(availability, date)
            .Where(window => window.EndLocalDateTime.Date > window.StartLocalDateTime.Date)
            .ToArray();
    }

    private static WindowOccurrence CreateOccurrence(
        DateOnly date,
        TimeSpan startLocalTime,
        TimeSpan endLocalTime,
        int slotDurationMinutes)
    {
        var start = date.ToDateTime(TimeOnly.FromTimeSpan(startLocalTime));
        var endDate = endLocalTime > startLocalTime ? date : date.AddDays(1);
        var end = endDate.ToDateTime(TimeOnly.FromTimeSpan(endLocalTime));

        return new WindowOccurrence(start, end, slotDurationMinutes);
    }

    private static (double? Latitude, double? Longitude) ResolveServiceAreaCenter(CatalogBranchReadModel branch)
    {
        if (branch.ServiceAreaCenterLatitude.HasValue && branch.ServiceAreaCenterLongitude.HasValue)
        {
            return (branch.ServiceAreaCenterLatitude, branch.ServiceAreaCenterLongitude);
        }

        return (branch.Latitude, branch.Longitude);
    }

    private static double CalculateDistanceKm(
        double startLatitude,
        double startLongitude,
        double endLatitude,
        double endLongitude)
    {
        var deltaLatitude = DegreesToRadians(endLatitude - startLatitude);
        var deltaLongitude = DegreesToRadians(endLongitude - startLongitude);
        var startLatitudeRadians = DegreesToRadians(startLatitude);
        var endLatitudeRadians = DegreesToRadians(endLatitude);

        var haversine = Math.Pow(Math.Sin(deltaLatitude / 2d), 2d)
            + Math.Cos(startLatitudeRadians)
            * Math.Cos(endLatitudeRadians)
            * Math.Pow(Math.Sin(deltaLongitude / 2d), 2d);

        var centralAngle = 2d * Math.Asin(Math.Min(1d, Math.Sqrt(haversine)));
        return EarthRadiusKm * centralAngle;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);

    private static DateTime AsUnspecified(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static bool IsEligibleBranch(CatalogBranchReadModel branch) =>
        branch.HasPublishedServiceArea && branch.ServiceAreaRadiusKm.HasValue;

    private static void ValidateRequestStructure(CheckoutDraftMutationRequestBase request)
    {
        if (request.Vehicle is null)
        {
            throw CreateInvalidRequest("vehicle", CheckoutDraftFieldErrorCodes.Required);
        }

        if (request.Location is null)
        {
            throw CreateInvalidRequest("location", CheckoutDraftFieldErrorCodes.Required);
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw CreateInvalidRequest("items", CheckoutDraftFieldErrorCodes.CollectionRequired);
        }
    }

    private static (CatalogBranchReadModel? Branch, HashSet<Guid> EligibleBranchIds) ResolveEligibleBranch(
        CatalogProviderReadModel provider,
        Guid branchSourceId)
    {
        var eligibleBranchIds = provider.Branches
            .Where(IsEligibleBranch)
            .Select(branch => branch.Id)
            .ToHashSet();
        var branch = provider.Branches
            .SingleOrDefault(item =>
                item.SourceBranchId == branchSourceId &&
                eligibleBranchIds.Contains(item.Id));

        return (branch, eligibleBranchIds);
    }

    private static bool RequiresAvailabilityRefresh(CatalogBranchReadModel branch) =>
        DeserializeAvailability(branch) is null;

    private void EnsureActive(CheckoutDraft draft)
    {
        if (draft.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            throw new CheckoutDraftException(
                CheckoutDraftProblemCodes.Expired,
                StatusCodes.Status410Gone,
                "The checkout draft has expired.");
        }
    }

    internal static CheckoutDraftResponse MapResponse(CheckoutDraft draft, string language)
    {
        return new CheckoutDraftResponse
        {
            Language = language,
            OrderGuid = draft.OrderGuid,
            Version = draft.PublicVersion,
            ExpiresAt = draft.ExpiresAt,
            RequiresReprice = draft.RequiresReprice,
            Pricing = draft.PricingSnapshot is null
                ? null
                : new CheckoutPricingSnapshotResponse
                {
                    CatalogVersion = draft.PricingSnapshot.CatalogVersion,
                    Currency = draft.PricingSnapshot.Currency,
                    QuotedAtUtc = draft.PricingSnapshot.QuotedAtUtc,
                    BaseSubtotal = draft.PricingSnapshot.BaseSubtotal,
                    AddonSubtotal = draft.PricingSnapshot.AddonSubtotal,
                    ItemSubtotal = draft.PricingSnapshot.ItemSubtotal,
                    ServiceFee = draft.PricingSnapshot.ServiceFee,
                    ServiceFeeMode = draft.PricingSnapshot.ServiceFeeMode,
                    ServiceFeeFlatAmount = draft.PricingSnapshot.ServiceFeeFlatAmount,
                    ServiceFeePercentageRate = draft.PricingSnapshot.ServiceFeePercentageRate,
                    TaxableSubtotal = draft.PricingSnapshot.TaxableSubtotal,
                    TaxRatePercent = draft.PricingSnapshot.TaxRatePercent,
                    TaxAppliesToServiceFee = draft.PricingSnapshot.TaxAppliesToServiceFee,
                    Tax = draft.PricingSnapshot.Tax,
                    GrandTotal = draft.PricingSnapshot.GrandTotal,
                    TotalDurationMinutes = draft.PricingSnapshot.TotalDurationMinutes,
                    Items = draft.PricingSnapshot.Items
                        .OrderBy(item => item.DisplayOrder)
                        .Select(item => new CheckoutPricingItemSnapshotResponse
                        {
                            OfferingSourceId = item.OfferingSourceId,
                            BaseSubtotal = item.BaseSubtotal,
                            AddonSubtotal = item.AddonSubtotal,
                            ItemSubtotal = item.ItemSubtotal,
                            TotalDurationMinutes = item.TotalDurationMinutes,
                            Selections = item.Selections
                                .OrderBy(selection => selection.DisplayOrder)
                                .Select(selection => new CheckoutPricingSelectionSnapshotResponse
                                {
                                    AddonGroupSourceId = selection.AddonGroupSourceId,
                                    AddonChoiceSourceId = selection.AddonChoiceSourceId,
                                    SelectionType = selection.SelectionType,
                                    Quantity = selection.Quantity,
                                    UnitPriceAdjustment = selection.UnitPriceAdjustment,
                                    TotalPriceAdjustment = selection.TotalPriceAdjustment,
                                    UnitDurationAdjustmentMinutes = selection.UnitDurationAdjustmentMinutes,
                                    TotalDurationAdjustmentMinutes = selection.TotalDurationAdjustmentMinutes,
                                    IsDefaultApplied = selection.IsDefaultApplied
                                })
                                .ToArray()
                        })
                        .ToArray()
                },
            Intent = new CheckoutDraftIntentResponse
            {
                BusinessSourceId = draft.BusinessSourceId,
                BranchSourceId = draft.BranchSourceId,
                CatalogVersion = draft.CatalogVersion,
                RequestedSlotStartUtc = draft.RequestedSlotStartUtc,
                Vehicle = new CheckoutDraftVehicleResponse
                {
                    VehicleType = draft.VehicleType,
                    LicensePlate = draft.LicensePlate,
                    Make = draft.VehicleMake,
                    Model = draft.VehicleModel,
                    Color = draft.VehicleColor
                },
                Location = new CheckoutDraftLocationResponse
                {
                    AddressLine = draft.AddressLine,
                    City = draft.City,
                    Area = draft.Area,
                    Latitude = (double)draft.Latitude,
                    Longitude = (double)draft.Longitude
                },
                Items = draft.Items
                    .OrderBy(item => item.DisplayOrder)
                    .Select(item => new CheckoutDraftItemResponse
                    {
                        OfferingSourceId = item.OfferingSourceId,
                        Selections = item.Selections
                            .OrderBy(selection => selection.DisplayOrder)
                            .Select(selection => new CheckoutDraftSelectionResponse
                            {
                                AddonGroupSourceId = selection.AddonGroupSourceId,
                                AddonChoiceSourceId = selection.AddonChoiceSourceId,
                                Quantity = selection.Quantity
                            })
                            .ToArray()
                    })
                    .ToArray()
            }
        };
    }

    private static decimal ToCoordinate(double value) =>
        decimal.Round((decimal)value, 6, MidpointRounding.AwayFromZero);

    private static CatalogSelectionType NormalizeSelectionType(string value) =>
        value switch
        {
            "SingleChoice" => CatalogSelectionType.SingleChoice,
            "MultipleChoice" or "Multiple" => CatalogSelectionType.MultipleChoice,
            "QuantityCounter" => CatalogSelectionType.QuantityCounter,
            "FixedIncludedChoice" => CatalogSelectionType.FixedIncludedChoice,
            "SegmentedSingleButtonChoice" => CatalogSelectionType.SegmentedSingleButtonChoice,
            _ => CatalogSelectionType.Unknown
        };

    private static CheckoutDraftException CreateSelectionInvalid(string field) =>
        new(
            CheckoutDraftProblemCodes.SelectionInvalid,
            StatusCodes.Status400BadRequest,
            "The supplied catalog selections are invalid or unavailable.",
            SingleFieldError(field, CheckoutDraftProblemCodes.SelectionInvalid));

    private static CheckoutDraftException CreateInvalidRequest(string field, string fieldCode) =>
        new(
            CheckoutDraftProblemCodes.Invalid,
            StatusCodes.Status400BadRequest,
            "The checkout draft request is invalid.",
            SingleFieldError(field, fieldCode));

    private static CheckoutDraftException CreateSlotUnavailable() =>
        new(
            CheckoutDraftProblemCodes.SlotUnavailable,
            StatusCodes.Status400BadRequest,
            "The requested slot is unavailable.",
            SingleFieldError("requestedSlotStartUtc", CheckoutDraftProblemCodes.SlotUnavailable));

    private static CheckoutDraftException CreateNotFound() =>
        new(
            CheckoutDraftProblemCodes.NotFound,
            StatusCodes.Status404NotFound,
            "The checkout draft was not found.");

    private static CheckoutDraftException CreateVersionConflict() =>
        new(
            CheckoutDraftProblemCodes.VersionConflict,
            StatusCodes.Status409Conflict,
            "The checkout draft version does not match the current record.",
            SingleFieldError("expectedVersion", CheckoutDraftProblemCodes.VersionConflict));

    private static IReadOnlyDictionary<string, string[]> SingleFieldError(string field, string code) =>
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [code]
        };

    private sealed record ChoiceContext(
        CatalogAddonGroupReadModel Group,
        CatalogAddonChoiceReadModel Choice);

    private sealed record GroupSelectionState(
        CatalogAddonChoiceReadModel Choice,
        int Quantity);

    private sealed record NormalizedDraftSelection(
        Guid AddonGroupSourceId,
        Guid AddonChoiceSourceId,
        int Quantity);

    private sealed record NormalizedDraftItem(
        Guid OfferingSourceId,
        IReadOnlyList<NormalizedDraftSelection> Selections);

    private sealed record NormalizedDraftIntent(
        Guid BusinessSourceId,
        Guid BranchSourceId,
        long CatalogVersion,
        DateTimeOffset RequestedSlotStartUtc,
        string VehicleType,
        string? LicensePlate,
        string? VehicleMake,
        string? VehicleModel,
        string? VehicleColor,
        string AddressLine,
        string? City,
        string? Area,
        double Latitude,
        double Longitude,
        IReadOnlyList<NormalizedDraftItem> Items);

    private sealed record WindowOccurrence(
        DateTime StartLocalDateTime,
        DateTime EndLocalDateTime,
        int SlotDurationMinutes);

    private enum CatalogSelectionType
    {
        Unknown = 0,
        SingleChoice = 1,
        MultipleChoice = 2,
        QuantityCounter = 3,
        FixedIncludedChoice = 4,
        SegmentedSingleButtonChoice = 5
    }
}
