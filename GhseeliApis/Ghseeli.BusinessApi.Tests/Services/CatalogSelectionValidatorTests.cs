using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Services.Catalog;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies authoritative add-on selection validation, normalization, and price rounding.
/// </summary>
public class CatalogSelectionValidatorTests
{
    private readonly CatalogSelectionValidator _validator =
        new(new CatalogRuleValidator());

    [Fact]
    public void Validate_WhenQuantityCounterSelectionsAreValid_ReturnsRoundedTotals()
    {
        var offering = CreateOffering();
        var choiceId = offering.AddonGroups.Single().Choices.Single().Id;

        var result = _validator.Validate(offering,
        [
            new ValidateAppointmentAddonSelectionRequest
            {
                AddonChoiceId = choiceId,
                Quantity = 2
            }
        ]);

        result.Errors.Should().BeEmpty();
        result.NormalizedSelections.Should().ContainSingle();
        result.AddonSubtotal.Should().Be(20.02m);
        result.DurationAdjustmentMinutes.Should().Be(30);
        result.NormalizedSelections.Single().TotalPriceAdjustment.Should().Be(20.02m);
    }

    [Fact]
    public void Validate_WhenUnknownChoiceIsSelected_ReturnsStableError()
    {
        var offering = CreateOffering();

        var result = _validator.Validate(offering,
        [
            new ValidateAppointmentAddonSelectionRequest
            {
                AddonChoiceId = Guid.NewGuid(),
                Quantity = 1
            }
        ]);

        result.Errors.Should().ContainSingle(error =>
            error.Code == AppointmentValidationErrorCodes.UnknownAddonChoice);
    }

    [Fact]
    public void Validate_WhenRequiredSingleChoiceIsMissing_ReturnsSelectionRuleViolation()
    {
        var offering = CreateOffering(selectionType: AddonSelectionType.SingleChoice, isRequired: true, minimumSelections: 1, maximumSelections: 1);

        var result = _validator.Validate(offering, Array.Empty<ValidateAppointmentAddonSelectionRequest>());

        result.Errors.Should().ContainSingle(error =>
            error.Code == AppointmentValidationErrorCodes.AddonSelectionRuleViolation);
    }

    [Fact]
    public void Validate_WhenChoiceBelongsToAnotherOffering_ReturnsUnknownChoice()
    {
        var offering = CreateOffering();
        var otherOffering = CreateOffering();
        var foreignChoiceId = otherOffering.AddonGroups.Single().Choices.Single().Id;

        var result = _validator.Validate(offering,
        [
            new ValidateAppointmentAddonSelectionRequest
            {
                AddonChoiceId = foreignChoiceId,
                Quantity = 1
            }
        ]);

        result.Errors.Should().ContainSingle(error =>
            error.Code == AppointmentValidationErrorCodes.UnknownAddonChoice);
    }

    [Fact]
    public void Validate_WhenFixedIncludedChoiceIsTampered_ReturnsSelectionRuleViolation()
    {
        var offering = CreateFixedIncludedOffering();
        var choiceId = offering.AddonGroups.Single().Choices.Single().Id;

        var result = _validator.Validate(offering,
        [
            new ValidateAppointmentAddonSelectionRequest
            {
                AddonChoiceId = choiceId,
                Quantity = 2
            }
        ]);

        result.Errors.Should().ContainSingle(error =>
            error.Code == AppointmentValidationErrorCodes.AddonSelectionRuleViolation);
    }

    [Fact]
    public void Validate_WhenAggregateDurationWouldOverflow_ReturnsStableSelectionViolation()
    {
        var offering = CreateOffering(priceAdjustment: 1_000_000m, durationAdjustmentMinutes: int.MaxValue);
        var choiceId = offering.AddonGroups.Single().Choices.Single().Id;

        var result = _validator.Validate(offering,
        [
            new ValidateAppointmentAddonSelectionRequest
            {
                AddonChoiceId = choiceId,
                Quantity = 2
            }
        ]);

        result.Errors.Should().Contain(error =>
            error.Code == AppointmentValidationErrorCodes.AddonSelectionRuleViolation);
    }

    private static ServiceOffering CreateOffering(
        AddonSelectionType selectionType = AddonSelectionType.QuantityCounter,
        bool isRequired = false,
        int minimumSelections = 0,
        int? maximumSelections = 3,
        decimal priceAdjustment = 10.005m,
        int durationAdjustmentMinutes = 15)
    {
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة",
            IsActive = true
        };
        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "تنظيف",
            IsActive = true
        };
        var offering = new ServiceOffering
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Category = category,
            NameAr = "غسيل",
            BasePrice = 49.995m,
            DurationMinutes = 45,
            IsActive = true
        };
        var group = new AddonGroup
        {
            Id = Guid.NewGuid(),
            ServiceOfferingId = offering.Id,
            ServiceOffering = offering,
            NameAr = "إضافات",
            SelectionType = selectionType,
            IsRequired = isRequired,
            MinimumSelections = minimumSelections,
            MaximumSelections = maximumSelections,
            IsActive = true
        };
        group.Choices.Add(new AddonChoice
        {
            Id = Guid.NewGuid(),
            AddonGroupId = group.Id,
            AddonGroup = group,
            NameAr = "شمع",
            PriceAdjustment = priceAdjustment,
            DurationAdjustmentMinutes = durationAdjustmentMinutes,
            DefaultQuantity = 0,
            IsActive = true
        });
        offering.AddonGroups.Add(group);
        return offering;
    }

    private static ServiceOffering CreateFixedIncludedOffering()
    {
        var offering = CreateOffering(
            selectionType: AddonSelectionType.FixedIncludedChoice,
            isRequired: true,
            minimumSelections: 1,
            maximumSelections: 1,
            priceAdjustment: 0m,
            durationAdjustmentMinutes: 0);

        var choice = offering.AddonGroups.Single().Choices.Single();
        choice.DefaultQuantity = 1;
        return offering;
    }
}
