using FluentValidation;
using Ghseeli.BusinessApi.Validators;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Validation;

namespace Ghseeli.BusinessApi.Validators.Catalog;

public class CreateServiceCategoryRequestValidator : AbstractValidator<CreateServiceCategoryRequest>
{
    public CreateServiceCategoryRequestValidator()
    {
        RuleFor(request => request.CompanyId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("Company id cannot be empty when supplied.");

        RuleFor(request => request.NameAr)
            .RequiredTrimmedText("Arabic category name", 200);

        RuleFor(request => request.NameHe)
            .OptionalTrimmedText("Hebrew category name", 200);

        RuleFor(request => request.DescriptionAr)
            .OptionalTrimmedText("Arabic category description", 1000);

        RuleFor(request => request.DescriptionHe)
            .OptionalTrimmedText("Hebrew category description", 1000);

        RuleFor(request => request.ImageUrl)
            .Must(CategoryPresentationValidation.IsValidImageUrl)
            .WithMessage("Category image URL must be an absolute HTTPS URL without credentials and 500 characters or fewer.");

        RuleFor(request => request.ColorHex)
            .Must(CategoryPresentationValidation.IsValidColorHex)
            .WithMessage("Category color must use #RRGGBB format.");

        RuleFor(request => request.DisplayOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Display order cannot be negative.");
    }
}
