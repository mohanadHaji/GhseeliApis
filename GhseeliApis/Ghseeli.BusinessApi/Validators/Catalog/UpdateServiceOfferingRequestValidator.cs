using FluentValidation;
using Ghseeli.BusinessApi.Validators;
using Ghseeli.BusinessApi.DTOs.Catalog;

namespace Ghseeli.BusinessApi.Validators.Catalog;

public class UpdateServiceOfferingRequestValidator : AbstractValidator<UpdateServiceOfferingRequest>
{
    public UpdateServiceOfferingRequestValidator()
    {
        RuleFor(request => request.BranchId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("Branch id cannot be empty when supplied.");

        RuleFor(request => request.NameAr)
            .RequiredTrimmedText("Arabic offering name", 200);

        RuleFor(request => request.NameHe)
            .OptionalTrimmedText("Hebrew offering name", 200);

        RuleFor(request => request.DescriptionAr)
            .OptionalTrimmedText("Arabic offering description", 1000);

        RuleFor(request => request.DescriptionHe)
            .OptionalTrimmedText("Hebrew offering description", 1000);

        RuleFor(request => request.BasePrice)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("Base price cannot be negative.");

        RuleFor(request => request.DurationMinutes)
            .GreaterThan(0)
            .WithMessage("Duration must be greater than zero minutes.");

        RuleFor(request => request.ImageUrl)
            .OptionalTrimmedText("Image URL", 500);

        RuleFor(request => request.ReferenceCode)
            .OptionalTrimmedText("Reference code", 100);

        RuleFor(request => request.DisplayOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Display order cannot be negative.");
    }
}
