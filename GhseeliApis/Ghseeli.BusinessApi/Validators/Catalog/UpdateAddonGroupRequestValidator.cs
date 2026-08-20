using FluentValidation;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Validators;
using Ghseeli.BusinessApi.DTOs.Catalog;

namespace Ghseeli.BusinessApi.Validators.Catalog;

public class UpdateAddonGroupRequestValidator : AbstractValidator<UpdateAddonGroupRequest>
{
    public UpdateAddonGroupRequestValidator()
    {
        RuleFor(request => request.NameAr)
            .RequiredTrimmedText("Arabic add-on group name", 200);

        RuleFor(request => request.NameHe)
            .OptionalTrimmedText("Hebrew add-on group name", 200);

        RuleFor(request => request.DescriptionAr)
            .OptionalTrimmedText("Arabic add-on group description", 1000);

        RuleFor(request => request.DescriptionHe)
            .OptionalTrimmedText("Hebrew add-on group description", 1000);

        RuleFor(request => request.SelectionType)
            .IsInEnum()
            .WithMessage("Selection type is invalid.");

        RuleFor(request => request.MinimumSelections)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Minimum selections cannot be negative.")
            .LessThanOrEqualTo(BusinessValueLimits.MaximumSelectionQuantity)
            .WithMessage($"Minimum selections must be {BusinessValueLimits.MaximumSelectionQuantity} or fewer.");

        RuleFor(request => request.MaximumSelections)
            .Must(value => !value.HasValue || (value.Value >= 1 &&
                value.Value <= BusinessValueLimits.MaximumSelectionQuantity))
            .WithMessage(
                $"Maximum selections must be between 1 and {BusinessValueLimits.MaximumSelectionQuantity} when provided.");

        RuleFor(request => request.DisplayOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Display order cannot be negative.");
    }
}
