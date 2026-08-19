using FluentValidation;
using Ghseeli.BusinessApi.Validators;
using Ghseeli.BusinessApi.DTOs.Catalog;

namespace Ghseeli.BusinessApi.Validators.Catalog;

public class CreateAddonChoiceRequestValidator : AbstractValidator<CreateAddonChoiceRequest>
{
    public CreateAddonChoiceRequestValidator()
    {
        RuleFor(request => request.NameAr)
            .RequiredTrimmedText("Arabic add-on choice name", 200);

        RuleFor(request => request.NameHe)
            .OptionalTrimmedText("Hebrew add-on choice name", 200);

        RuleFor(request => request.DescriptionAr)
            .OptionalTrimmedText("Arabic add-on choice description", 1000);

        RuleFor(request => request.DescriptionHe)
            .OptionalTrimmedText("Hebrew add-on choice description", 1000);

        RuleFor(request => request.PriceAdjustment)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("Price adjustment cannot be negative.");

        RuleFor(request => request.DurationAdjustmentMinutes)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Duration adjustment cannot be negative.");

        RuleFor(request => request.DefaultQuantity)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Default quantity cannot be negative.");

        RuleFor(request => request.DisplayOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Display order cannot be negative.");
    }
}
