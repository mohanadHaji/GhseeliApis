using FluentAssertions;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Validators.Catalog;

namespace Ghseeli.BusinessApi.Tests.Validators.Catalog;

/// <summary>
/// Verifies FluentValidation request rules for catalog endpoints.
/// </summary>
public class CatalogRequestValidatorsTests
{
    [Fact]
    public void CreateServiceCategoryRequestValidator_AllowsMissingHebrewName()
    {
        var validator = new CreateServiceCategoryRequestValidator();
        var request = new CreateServiceCategoryRequest
        {
            NameAr = "غسيل",
            DisplayOrder = 0,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateServiceCategoryRequestValidator_RejectsMissingArabicName()
    {
        var validator = new UpdateServiceCategoryRequestValidator();
        var request = new UpdateServiceCategoryRequest
        {
            NameAr = " ",
            DisplayOrder = 0,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateServiceCategoryRequest.NameAr));
    }

    [Fact]
    public void CreateServiceOfferingRequestValidator_RejectsSuppliedHebrewNameLongerThanMaxLength()
    {
        var validator = new CreateServiceOfferingRequestValidator();
        var request = new CreateServiceOfferingRequest
        {
            CategoryId = Guid.NewGuid(),
            NameAr = "غسيل خارجي",
            NameHe = new string('א', 201),
            BasePrice = 50m,
            DurationMinutes = 30,
            DisplayOrder = 1,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateServiceOfferingRequest.NameHe));
    }

    [Fact]
    public void UpdateServiceOfferingRequestValidator_RejectsNegativeBasePrice()
    {
        var validator = new UpdateServiceOfferingRequestValidator();
        var request = new UpdateServiceOfferingRequest
        {
            NameAr = "غسيل خارجي",
            BasePrice = -1m,
            DurationMinutes = 30,
            DisplayOrder = 1,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateServiceOfferingRequest.BasePrice));
    }

    [Fact]
    public void CreateAddonGroupRequestValidator_ValidatesNestedChoiceArabicName()
    {
        var validator = new CreateAddonGroupRequestValidator(new CreateAddonChoiceRequestValidator());
        var request = new CreateAddonGroupRequest
        {
            NameAr = "إضافات",
            SelectionType = AddonSelectionType.MultipleChoice,
            MinimumSelections = 0,
            MaximumSelections = 3,
            DisplayOrder = 0,
            IsActive = true,
            Choices =
            [
                new CreateAddonChoiceRequest
                {
                    NameAr = " ",
                    PriceAdjustment = 10m,
                    DurationAdjustmentMinutes = 5,
                    DefaultQuantity = 0,
                    DisplayOrder = 0,
                    IsActive = true
                }
            ]
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName.Contains(nameof(CreateAddonChoiceRequest.NameAr), StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateAddonGroupRequestValidator_RejectsMaximumSelectionsBelowOne()
    {
        var validator = new UpdateAddonGroupRequestValidator();
        var request = new UpdateAddonGroupRequest
        {
            NameAr = "إضافات",
            SelectionType = AddonSelectionType.MultipleChoice,
            MinimumSelections = 0,
            MaximumSelections = 0,
            DisplayOrder = 0,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateAddonGroupRequest.MaximumSelections));
    }

    [Fact]
    public void CreateAddonChoiceRequestValidator_AllowsMissingHebrewName()
    {
        var validator = new CreateAddonChoiceRequestValidator();
        var request = new CreateAddonChoiceRequest
        {
            NameAr = "تعقيم",
            PriceAdjustment = 10m,
            DurationAdjustmentMinutes = 5,
            DefaultQuantity = 0,
            DisplayOrder = 0,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateAddonChoiceRequestValidator_RejectsNegativeDisplayOrder()
    {
        var validator = new UpdateAddonChoiceRequestValidator();
        var request = new UpdateAddonChoiceRequest
        {
            NameAr = "تعقيم",
            PriceAdjustment = 10m,
            DurationAdjustmentMinutes = 5,
            DefaultQuantity = 0,
            DisplayOrder = -1,
            IsActive = true
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateAddonChoiceRequest.DisplayOrder));
    }
}
