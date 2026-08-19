using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Catalog;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies focused catalog domain-rule validation across selection types.
/// </summary>
public class CatalogRuleValidatorTests
{
    private readonly CatalogRuleValidator _validator = new();

    [Fact]
    public void ValidateAddonGroup_SingleChoice_AllowsSingleDefault()
    {
        var group = CreateGroup(AddonSelectionType.SingleChoice, isRequired: true, minimumSelections: 1, maximumSelections: 1);
        group.Choices.Add(CreateChoice("قياسي", defaultQuantity: 1));

        var action = () => _validator.ValidateAddonGroup(group);

        action.Should().NotThrow();
    }

    [Fact]
    public void ValidateAddonGroup_SingleChoice_RejectsMultipleDefaults()
    {
        var group = CreateGroup(AddonSelectionType.SingleChoice, isRequired: true, minimumSelections: 1, maximumSelections: 1);
        group.Choices.Add(CreateChoice("قياسي", defaultQuantity: 1));
        group.Choices.Add(CreateChoice("مميز", defaultQuantity: 1, displayOrder: 1));

        var action = () => _validator.ValidateAddonGroup(group);

        action.Should().Throw<CatalogValidationException>()
            .WithMessage("*one default-selected active choice*");
    }

    [Fact]
    public void ValidateAddonGroup_MultipleChoice_RejectsDefaultCountAboveMaximum()
    {
        var group = CreateGroup(AddonSelectionType.MultipleChoice, isRequired: true, minimumSelections: 1, maximumSelections: 1);
        group.Choices.Add(CreateChoice("شمع", defaultQuantity: 1));
        group.Choices.Add(CreateChoice("تعقيم", defaultQuantity: 1, displayOrder: 1));

        var action = () => _validator.ValidateAddonGroup(group);

        action.Should().Throw<CatalogValidationException>()
            .WithMessage("*maximum selection rule*");
    }

    [Fact]
    public void ValidateAddonGroup_QuantityCounter_RejectsDefaultTotalAboveMaximum()
    {
        var group = CreateGroup(AddonSelectionType.QuantityCounter, isRequired: false, minimumSelections: 0, maximumSelections: 2);
        group.Choices.Add(CreateChoice("معطر", defaultQuantity: 2));
        group.Choices.Add(CreateChoice("مناديل", defaultQuantity: 1, displayOrder: 1));

        var action = () => _validator.ValidateAddonGroup(group);

        action.Should().Throw<CatalogValidationException>()
            .WithMessage("*maximum selection rule*");
    }

    [Fact]
    public void ValidateAddonGroup_FixedIncludedChoice_AllowsSingleIncludedChoice()
    {
        var group = CreateGroup(AddonSelectionType.FixedIncludedChoice, isRequired: true, minimumSelections: 1, maximumSelections: 1);
        group.Choices.Add(CreateChoice("قياسي", priceAdjustment: 0m, durationAdjustmentMinutes: 0, defaultQuantity: 1));

        var action = () => _validator.ValidateAddonGroup(group);

        action.Should().NotThrow();
    }

    [Fact]
    public void ValidateAddonGroup_FixedIncludedChoice_RejectsPriceAdjustments()
    {
        var group = CreateGroup(AddonSelectionType.FixedIncludedChoice, isRequired: true, minimumSelections: 1, maximumSelections: 1);
        group.Choices.Add(CreateChoice("قياسي", priceAdjustment: 5m, durationAdjustmentMinutes: 0, defaultQuantity: 1));

        var action = () => _validator.ValidateAddonGroup(group);

        action.Should().Throw<CatalogValidationException>()
            .WithMessage("*price or duration*");
    }

    [Fact]
    public void ValidateAddonGroup_SegmentedSingleButtonChoice_RejectsMaximumSelectionCountOtherThanOne()
    {
        var group = CreateGroup(AddonSelectionType.SegmentedSingleButtonChoice, isRequired: false, minimumSelections: 0, maximumSelections: 2);
        group.Choices.Add(CreateChoice("داخلي"));

        var action = () => _validator.ValidateAddonGroup(group);

        action.Should().Throw<CatalogValidationException>()
            .WithMessage("*maximum selection count of one*");
    }

    [Fact]
    public void ValidateAddonGroup_RejectsInactiveDefaultChoice()
    {
        var group = CreateGroup(AddonSelectionType.MultipleChoice, isRequired: false, minimumSelections: 0, maximumSelections: 3);
        group.IsActive = false;
        group.Choices.Add(CreateChoice("تعقيم", defaultQuantity: 1, isActive: false));

        var action = () => _validator.ValidateAddonGroup(group);

        action.Should().Throw<CatalogValidationException>()
            .WithMessage("*Inactive choices cannot be default-selected*");
    }

    private static AddonGroup CreateGroup(
        AddonSelectionType selectionType,
        bool isRequired,
        int minimumSelections,
        int? maximumSelections)
    {
        return new AddonGroup
        {
            Id = Guid.NewGuid(),
            ServiceOfferingId = Guid.NewGuid(),
            NameAr = "إضافات",
            SelectionType = selectionType,
            IsRequired = isRequired,
            MinimumSelections = minimumSelections,
            MaximumSelections = maximumSelections,
            DisplayOrder = 0,
            IsActive = true
        };
    }

    private static AddonChoice CreateChoice(
        string nameAr,
        decimal priceAdjustment = 0m,
        int durationAdjustmentMinutes = 0,
        int defaultQuantity = 0,
        int displayOrder = 0,
        bool isActive = true)
    {
        return new AddonChoice
        {
            Id = Guid.NewGuid(),
            NameAr = nameAr,
            PriceAdjustment = priceAdjustment,
            DurationAdjustmentMinutes = durationAdjustmentMinutes,
            DefaultQuantity = defaultQuantity,
            DisplayOrder = displayOrder,
            IsActive = isActive
        };
    }
}
