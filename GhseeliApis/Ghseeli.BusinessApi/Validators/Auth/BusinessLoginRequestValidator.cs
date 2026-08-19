using FluentValidation;
using Ghseeli.BusinessApi.Validators;
using Ghseeli.BusinessApi.DTOs.Auth;

namespace Ghseeli.BusinessApi.Validators.Auth;

public class BusinessLoginRequestValidator : AbstractValidator<BusinessLoginRequest>
{
    public BusinessLoginRequestValidator()
    {
        RuleFor(request => request.Email)
            .RequiredEmailAddress(200);

        RuleFor(request => request.Password)
            .Cascade(CascadeMode.Stop)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("Password is required.")
            .MaximumLength(100)
            .WithMessage("Password must be 100 characters or fewer.");
    }
}
