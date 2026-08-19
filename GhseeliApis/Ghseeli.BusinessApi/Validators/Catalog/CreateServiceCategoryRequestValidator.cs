using FluentValidation;
using Ghseeli.BusinessApi.Validators;
using Ghseeli.BusinessApi.DTOs.Catalog;

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

        RuleFor(request => request.DisplayOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Display order cannot be negative.");
    }
}
