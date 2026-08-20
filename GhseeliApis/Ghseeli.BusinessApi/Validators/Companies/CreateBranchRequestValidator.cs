using FluentValidation;
using Ghseeli.BusinessApi.Validators;
using Ghseeli.BusinessApi.DTOs.Companies;

namespace Ghseeli.BusinessApi.Validators.Companies;

public class CreateBranchRequestValidator : AbstractValidator<CreateBranchRequest>
{
    public CreateBranchRequestValidator()
    {
        RuleFor(request => request.NameAr)
            .RequiredTrimmedText("Arabic branch name", 200);

        RuleFor(request => request.NameHe)
            .OptionalTrimmedText("Hebrew branch name", 200);

        RuleFor(request => request.AddressAr)
            .RequiredTrimmedText("Arabic branch address", 300);

        RuleFor(request => request.AddressHe)
            .OptionalTrimmedText("Hebrew branch address", 300);

        RuleFor(request => request.Latitude)
            .Must(value => !value.HasValue || (value.Value >= -90d && value.Value <= 90d))
            .WithMessage("Latitude must be between -90 and 90.");

        RuleFor(request => request.Longitude)
            .Must(value => !value.HasValue || (value.Value >= -180d && value.Value <= 180d))
            .WithMessage("Longitude must be between -180 and 180.");

        RuleFor(request => request)
            .Must(request => request.Latitude.HasValue == request.Longitude.HasValue)
            .WithMessage("Latitude and longitude must be supplied together.");
    }
}
