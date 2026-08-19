using FluentValidation;
using Ghseeli.BusinessApi.Validators;
using Ghseeli.BusinessApi.DTOs.Catalog;

namespace Ghseeli.BusinessApi.Validators.Catalog;

public class UpdateServiceCategoryRequestValidator : AbstractValidator<UpdateServiceCategoryRequest>
{
    public UpdateServiceCategoryRequestValidator()
    {
        RuleFor(request => request.NameAr)
            .RequiredTrimmedText("Arabic category name", 200);

        RuleFor(request => request.NameHe)
            .OptionalTrimmedText("Hebrew category name", 200);

        RuleFor(request => request.DescriptionAr)
            .OptionalTrimmedText("Arabic category description", 1000);

        RuleFor(request => request.DescriptionHe)
            .OptionalTrimmedText("Hebrew category description", 1000);

        RuleFor(request => request.DisplayOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Display order cannot be negative.");
    }
}
