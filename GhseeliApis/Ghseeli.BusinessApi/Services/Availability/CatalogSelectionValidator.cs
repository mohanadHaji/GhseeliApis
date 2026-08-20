using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Services.Catalog;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Availability;

public class CatalogSelectionValidator : ICatalogSelectionValidator
{
    private readonly ICatalogRuleValidator _catalogRuleValidator;

    public CatalogSelectionValidator(ICatalogRuleValidator catalogRuleValidator)
    {
        _catalogRuleValidator = catalogRuleValidator;
    }

    public CatalogSelectionValidationResult Validate(
        ServiceOffering offering,
        IReadOnlyCollection<ValidateAppointmentAddonSelectionRequest> requestedSelections)
    {
        ArgumentNullException.ThrowIfNull(offering);

        var errors = new List<AppointmentValidationIssue>();
        var requestedByChoiceId = new Dictionary<Guid, (int Quantity, bool Explicit)>();

        foreach (var duplicate in requestedSelections
                     .GroupBy(selection => selection.AddonChoiceId)
                     .Where(group => group.Count() > 1))
        {
            errors.Add(Issue(
                AppointmentValidationErrorCodes.DuplicateAddonChoice,
                $"Add-on choice '{duplicate.Key}' was supplied more than once.",
                "selectedAddons"));
        }

        foreach (var selection in requestedSelections)
        {
            if (selection.Quantity > BusinessValueLimits.MaximumSelectionQuantity)
            {
                errors.Add(OverflowViolation(
                    $"Add-on quantities must be {BusinessValueLimits.MaximumSelectionQuantity} or fewer."));
                continue;
            }

            if (!requestedByChoiceId.ContainsKey(selection.AddonChoiceId))
            {
                requestedByChoiceId[selection.AddonChoiceId] = (selection.Quantity, true);
            }
        }

        var activeGroups = offering.AddonGroups
            .Where(group => group.IsActive)
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.NameAr)
            .ToArray();

        var allChoices = offering.AddonGroups
            .SelectMany(group => group.Choices.Select(choice => new { Group = group, Choice = choice }))
            .ToDictionary(item => item.Choice.Id, item => item);

        var effectiveQuantities = new Dictionary<Guid, (int Quantity, bool IsDefaultApplied)>();
        foreach (var choice in activeGroups
                     .SelectMany(group => group.Choices)
                     .Where(choice => choice.IsActive && choice.DefaultQuantity > 0))
        {
            effectiveQuantities[choice.Id] = (choice.DefaultQuantity, true);
        }

        foreach (var selection in requestedByChoiceId)
        {
            if (!allChoices.TryGetValue(selection.Key, out var choiceItem))
            {
                errors.Add(Issue(
                    AppointmentValidationErrorCodes.UnknownAddonChoice,
                    $"Add-on choice '{selection.Key}' does not belong to the selected offering.",
                    "selectedAddons"));
                continue;
            }

            if (!choiceItem.Group.IsActive || !choiceItem.Choice.IsActive)
            {
                errors.Add(Issue(
                    AppointmentValidationErrorCodes.InactiveAddonChoice,
                    $"Add-on choice '{selection.Key}' is inactive and cannot be selected.",
                    "selectedAddons"));
                continue;
            }

            effectiveQuantities[selection.Key] = (selection.Value.Quantity, false);
        }

        foreach (var group in activeGroups)
        {
            try
            {
                _catalogRuleValidator.ValidateAddonGroup(group);
            }
            catch (CatalogValidationException exception)
            {
                errors.Add(Issue(
                    AppointmentValidationErrorCodes.AddonSelectionRuleViolation,
                    exception.Message,
                    "selectedAddons"));
                continue;
            }

            var normalizedForGroup = group.Choices
                .Where(choice => choice.IsActive)
                .Select(choice =>
                {
                    effectiveQuantities.TryGetValue(choice.Id, out var value);
                    return new GroupSelectionState
                    {
                        Choice = choice,
                        Quantity = value.Quantity,
                        IsDefaultApplied = value.IsDefaultApplied
                    };
                })
                .Where(item => item.Quantity > 0)
                .OrderBy(item => item.Choice.DisplayOrder)
                .ThenBy(item => item.Choice.NameAr)
                .ToArray();

            ValidateGroupSelection(group, normalizedForGroup, errors);
        }

        if (errors.Count > 0)
        {
            return new CatalogSelectionValidationResult
            {
                Errors = errors
            };
        }

        try
        {
            var normalizedSelections = new List<NormalizedAddonSelection>();
            var addonSubtotal = 0m;
            var durationAdjustmentMinutes = 0;

            foreach (var group in activeGroups)
            {
                foreach (var choice in group.Choices
                             .Where(choice => choice.IsActive)
                             .OrderBy(choice => choice.DisplayOrder)
                             .ThenBy(choice => choice.NameAr))
                {
                    effectiveQuantities.TryGetValue(choice.Id, out var value);
                    if (value.Quantity <= 0)
                    {
                        continue;
                    }

                    var unitPrice = BusinessMoney.RoundToCurrency(choice.PriceAdjustment);
                    var totalPrice = BusinessMoney.RoundToCurrency(checked(unitPrice * value.Quantity));
                    var totalDuration = checked(choice.DurationAdjustmentMinutes * value.Quantity);

                    normalizedSelections.Add(new NormalizedAddonSelection
                    {
                        AddonGroupId = group.Id,
                        AddonChoiceId = choice.Id,
                        SelectionType = group.SelectionType.ToString(),
                        Quantity = value.Quantity,
                        UnitPriceAdjustment = unitPrice,
                        TotalPriceAdjustment = totalPrice,
                        UnitDurationAdjustmentMinutes = choice.DurationAdjustmentMinutes,
                        TotalDurationAdjustmentMinutes = totalDuration,
                        IsDefaultApplied = value.IsDefaultApplied
                    });

                    addonSubtotal = BusinessMoney.RoundToCurrency(checked(addonSubtotal + totalPrice));
                    durationAdjustmentMinutes = checked(durationAdjustmentMinutes + totalDuration);
                }
            }

            return new CatalogSelectionValidationResult
            {
                Errors = Array.Empty<AppointmentValidationIssue>(),
                NormalizedSelections = normalizedSelections,
                AddonSubtotal = addonSubtotal,
                DurationAdjustmentMinutes = durationAdjustmentMinutes
            };
        }
        catch (OverflowException)
        {
            return new CatalogSelectionValidationResult
            {
                Errors =
                [
                    OverflowViolation("The requested add-on quantities exceed the supported numeric limits.")
                ]
            };
        }
    }

    private static void ValidateGroupSelection(
        AddonGroup group,
        IReadOnlyCollection<GroupSelectionState> normalizedForGroup,
        ICollection<AppointmentValidationIssue> errors)
    {
        var selectedCount = normalizedForGroup.Count;
        var quantitySum = normalizedForGroup.Sum(item => item.Quantity);

        switch (group.SelectionType)
        {
            case AddonSelectionType.SingleChoice:
            case AddonSelectionType.SegmentedSingleButtonChoice:
                if (normalizedForGroup.Any(item => item.Quantity > 1))
                {
                    errors.Add(GroupViolation(group, "Single-choice groups can only select one quantity per choice."));
                    return;
                }

                if (selectedCount < group.MinimumSelections ||
                    (group.MaximumSelections.HasValue && selectedCount > group.MaximumSelections.Value))
                {
                    errors.Add(GroupViolation(group, "Selected choices do not satisfy the group's single-choice limits."));
                }

                break;

            case AddonSelectionType.MultipleChoice:
                if (normalizedForGroup.Any(item => item.Quantity > 1))
                {
                    errors.Add(GroupViolation(group, "Multiple-choice groups can only select quantities of zero or one."));
                    return;
                }

                if (selectedCount < group.MinimumSelections ||
                    (group.MaximumSelections.HasValue && selectedCount > group.MaximumSelections.Value))
                {
                    errors.Add(GroupViolation(group, "Selected choices do not satisfy the group's minimum or maximum selection rules."));
                }

                break;

            case AddonSelectionType.QuantityCounter:
                if (quantitySum < group.MinimumSelections ||
                    (group.MaximumSelections.HasValue && quantitySum > group.MaximumSelections.Value))
                {
                    errors.Add(GroupViolation(group, "Selected quantities do not satisfy the group's minimum or maximum quantity rules."));
                }

                break;

            case AddonSelectionType.FixedIncludedChoice:
                if (selectedCount != 1 || quantitySum != 1)
                {
                    errors.Add(GroupViolation(group, "Fixed included choice groups must keep exactly one included choice."));
                }

                break;

            default:
                errors.Add(GroupViolation(group, $"Unsupported add-on selection type '{group.SelectionType}'."));
                break;
        }
    }

    private static AppointmentValidationIssue GroupViolation(
        AddonGroup group,
        string message)
    {
        return Issue(
            AppointmentValidationErrorCodes.AddonSelectionRuleViolation,
            $"{message} GroupId={group.Id}.",
            "selectedAddons");
    }

    private static AppointmentValidationIssue OverflowViolation(string message)
    {
        return Issue(
            AppointmentValidationErrorCodes.AddonSelectionRuleViolation,
            message,
            "selectedAddons");
    }

    private static AppointmentValidationIssue Issue(
        string code,
        string message,
        string? field = null)
    {
        return new AppointmentValidationIssue
        {
            Code = code,
            Message = message,
            Field = field
        };
    }

    private sealed class GroupSelectionState
    {
        public AddonChoice Choice { get; init; } = null!;
        public int Quantity { get; init; }
        public bool IsDefaultApplied { get; init; }
    }
}
