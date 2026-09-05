using FluentValidation;
using GhseeliApis.DTOs.Checkout;

namespace GhseeliApis.Validators.Checkout;

public sealed class GetCheckoutDraftRequestValidator : AbstractValidator<GetCheckoutDraftRequest>
{
    public GetCheckoutDraftRequestValidator()
    {
        When(request => !string.IsNullOrWhiteSpace(request.Language), () =>
        {
            RuleFor(request => request.Language)
                .MustUseSupportedLanguage();
        });
    }
}
