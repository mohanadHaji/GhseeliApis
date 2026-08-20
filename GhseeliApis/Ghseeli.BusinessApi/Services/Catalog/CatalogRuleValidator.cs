using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Models;

namespace Ghseeli.BusinessApi.Services.Catalog;

public class CatalogRuleValidator : ICatalogRuleValidator
{
    public void ValidateAddonGroup(AddonGroup addonGroup)
    {
        ArgumentNullException.ThrowIfNull(addonGroup);

        if (addonGroup.MinimumSelections < 0)
        {
            throw CatalogValidationException.ForField(
                "minimumSelections",
                "Minimum selections cannot be negative.");
        }

        if (addonGroup.MinimumSelections > BusinessValueLimits.MaximumSelectionQuantity)
        {
            throw CatalogValidationException.ForField(
                "minimumSelections",
                $"Minimum selections must be {BusinessValueLimits.MaximumSelectionQuantity} or fewer.");
        }

        if (addonGroup.MaximumSelections.HasValue && addonGroup.MaximumSelections.Value < 1)
        {
            throw CatalogValidationException.ForField(
                "maximumSelections",
                "Maximum selections must be at least one when provided.");
        }

        if (addonGroup.MaximumSelections.HasValue &&
            addonGroup.MaximumSelections.Value > BusinessValueLimits.MaximumSelectionQuantity)
        {
            throw CatalogValidationException.ForField(
                "maximumSelections",
                $"Maximum selections must be {BusinessValueLimits.MaximumSelectionQuantity} or fewer.");
        }

        if (addonGroup.MaximumSelections.HasValue &&
            addonGroup.MaximumSelections.Value < addonGroup.MinimumSelections)
        {
            throw CatalogValidationException.ForField(
                "maximumSelections",
                "Maximum selections cannot be less than minimum selections.");
        }

        if (!addonGroup.IsRequired && addonGroup.MinimumSelections != 0)
        {
            throw CatalogValidationException.ForField(
                "minimumSelections",
                "Optional add-on groups must have a minimum selection count of zero.");
        }

        if (addonGroup.IsRequired && addonGroup.MinimumSelections < 1)
        {
            throw CatalogValidationException.ForField(
                "minimumSelections",
                "Required add-on groups must require at least one selection.");
        }

        foreach (var choice in addonGroup.Choices)
        {
            ValidateAddonChoice(choice);
        }

        var activeChoices = addonGroup.Choices
            .Where(choice => choice.IsActive)
            .OrderBy(choice => choice.DisplayOrder)
            .ThenBy(choice => choice.NameAr)
            .ToArray();

        if (addonGroup.IsActive && activeChoices.Length == 0)
        {
            throw CatalogValidationException.ForField(
                "choices",
                "Active add-on groups must contain at least one active choice.");
        }

        var activeDefaultChoices = activeChoices
            .Where(choice => choice.DefaultQuantity > 0)
            .ToArray();

        switch (addonGroup.SelectionType)
        {
            case AddonSelectionType.SingleChoice:
            case AddonSelectionType.SegmentedSingleButtonChoice:
                ValidateSingleChoiceGroup(addonGroup, activeDefaultChoices);
                break;

            case AddonSelectionType.MultipleChoice:
                if (addonGroup.Choices.Any(choice => choice.DefaultQuantity > 1))
                {
                    throw CatalogValidationException.ForField(
                        "defaultQuantity",
                        "Multiple-choice add-on choices can only use default quantities of zero or one.");
                }

                ValidateDefaultCountRange(
                    addonGroup.MinimumSelections,
                    addonGroup.MaximumSelections,
                    activeDefaultChoices.Length);
                break;

            case AddonSelectionType.QuantityCounter:
                ValidateDefaultCountRange(
                    addonGroup.MinimumSelections,
                    addonGroup.MaximumSelections,
                    checked(activeDefaultChoices.Sum(choice => choice.DefaultQuantity)));
                break;

            case AddonSelectionType.FixedIncludedChoice:
                ValidateFixedIncludedChoiceGroup(addonGroup, activeChoices);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported add-on selection type '{addonGroup.SelectionType}'.");
        }
    }

    private static void ValidateSingleChoiceGroup(
        AddonGroup addonGroup,
        IReadOnlyCollection<AddonChoice> activeDefaultChoices)
    {
        if (addonGroup.MaximumSelections != 1)
        {
            throw CatalogValidationException.ForField(
                "maximumSelections",
                "Single-choice add-on groups must have a maximum selection count of one.");
        }

        var expectedMinimum = addonGroup.IsRequired ? 1 : 0;
        if (addonGroup.MinimumSelections != expectedMinimum)
        {
            throw CatalogValidationException.ForField(
                "minimumSelections",
                "Single-choice add-on groups must have a minimum selection count that matches the required setting.");
        }

        if (addonGroup.Choices.Any(choice => choice.DefaultQuantity > 1))
        {
            throw CatalogValidationException.ForField(
                "defaultQuantity",
                "Single-choice add-on choices can only use default quantities of zero or one.");
        }

        if (activeDefaultChoices.Count > 1)
        {
            throw CatalogValidationException.ForField(
                "defaultQuantity",
                "Single-choice add-on groups can only have one default-selected active choice.");
        }
    }

    private static void ValidateFixedIncludedChoiceGroup(
        AddonGroup addonGroup,
        IReadOnlyCollection<AddonChoice> activeChoices)
    {
        if (!addonGroup.IsRequired)
        {
            throw CatalogValidationException.ForField(
                "isRequired",
                "Fixed included choices must always be required.");
        }

        if (addonGroup.MinimumSelections != 1 || addonGroup.MaximumSelections != 1)
        {
            throw CatalogValidationException.ForField(
                "minimumSelections",
                "Fixed included choices must require exactly one included active choice.");
        }

        if (activeChoices.Count != 1)
        {
            throw CatalogValidationException.ForField(
                "choices",
                "Fixed included choice groups must contain exactly one active choice.");
        }

        var includedChoice = activeChoices.Single();
        if (includedChoice.DefaultQuantity != 1)
        {
            throw CatalogValidationException.ForField(
                "defaultQuantity",
                "Fixed included choice groups must mark the active choice as the single default selection.");
        }

        if (includedChoice.PriceAdjustment != 0m ||
            includedChoice.DurationAdjustmentMinutes != 0)
        {
            throw CatalogValidationException.ForField(
                "priceAdjustment",
                "Fixed included choices cannot change price or duration.");
        }

        if (addonGroup.Choices.Any(choice =>
                choice.Id != includedChoice.Id && choice.DefaultQuantity > 0))
        {
            throw CatalogValidationException.ForField(
                "defaultQuantity",
                "Fixed included choice groups cannot assign defaults to additional choices.");
        }
    }

    private static void ValidateDefaultCountRange(
        int minimumSelections,
        int? maximumSelections,
        int defaultCount)
    {
        if (defaultCount == 0)
        {
            return;
        }

        if (defaultCount < minimumSelections)
        {
            throw CatalogValidationException.ForField(
                "defaultQuantity",
                "Default selections must satisfy the group's minimum selection rule when provided.");
        }

        if (maximumSelections.HasValue && defaultCount > maximumSelections.Value)
        {
            throw CatalogValidationException.ForField(
                "defaultQuantity",
                "Default selections cannot exceed the group's maximum selection rule.");
        }
    }

    private static void ValidateAddonChoice(AddonChoice addonChoice)
    {
        if (addonChoice.PriceAdjustment < 0)
        {
            throw CatalogValidationException.ForField(
                "priceAdjustment",
                "Price adjustment cannot be negative.");
        }

        if (!BusinessMoney.IsWithinSupportedRange(addonChoice.PriceAdjustment))
        {
            throw CatalogValidationException.ForField(
                "priceAdjustment",
                $"Price adjustment must be between 0.00 and {BusinessValueLimits.MaximumMoneyAmount:0.00} after rounding to two decimal places.");
        }

        if (addonChoice.DurationAdjustmentMinutes < 0)
        {
            throw CatalogValidationException.ForField(
                "durationAdjustmentMinutes",
                "Duration adjustment cannot be negative.");
        }

        if (addonChoice.DurationAdjustmentMinutes > BusinessValueLimits.MaximumDurationMinutes)
        {
            throw CatalogValidationException.ForField(
                "durationAdjustmentMinutes",
                $"Duration adjustment must be {BusinessValueLimits.MaximumDurationMinutes} minutes or fewer.");
        }

        if (addonChoice.DefaultQuantity < 0)
        {
            throw CatalogValidationException.ForField(
                "defaultQuantity",
                "Default quantity cannot be negative.");
        }

        if (addonChoice.DefaultQuantity > BusinessValueLimits.MaximumSelectionQuantity)
        {
            throw CatalogValidationException.ForField(
                "defaultQuantity",
                $"Default quantity must be {BusinessValueLimits.MaximumSelectionQuantity} or fewer.");
        }

        if (addonChoice.DisplayOrder < 0)
        {
            throw CatalogValidationException.ForField(
                "displayOrder",
                "Display order cannot be negative.");
        }

        if (!addonChoice.IsActive && addonChoice.DefaultQuantity > 0)
        {
            throw CatalogValidationException.ForField(
                "defaultQuantity",
                "Inactive choices cannot be default-selected.");
        }
    }
}
