using FluentValidation;
using Ghseeli.BusinessApi.Validators;
using Ghseeli.BusinessApi.DTOs.Auth;

namespace Ghseeli.BusinessApi.Validators.Auth;

public class RegisterOwnerRequestValidator : AbstractValidator<RegisterOwnerRequest>
{
    public RegisterOwnerRequestValidator()
    {
        RuleFor(request => request.Email)
            .RequiredEmailAddress(200);

        RuleFor(request => request.Password)
            .Cascade(CascadeMode.Stop)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("Password is required.")
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters.")
            .MaximumLength(100)
            .WithMessage("Password must be 100 characters or fewer.");

        RuleFor(request => request.FullName)
            .RequiredTrimmedText("Full name", 150);

        RuleFor(request => request.PhoneNumber)
            .OptionalTrimmedText("Phone number", 30);

        RuleFor(request => request.CompanyNameAr)
            .RequiredTrimmedText("Arabic company name", 200);

        RuleFor(request => request.CompanyNameHe)
            .OptionalTrimmedText("Hebrew company name", 200);
    }
}
