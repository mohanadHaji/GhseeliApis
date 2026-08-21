using FluentValidation;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Services.Checkout;

namespace GhseeliApis.Validators.Checkout;

public sealed class UpdateCheckoutDraftRequestValidator
    : CheckoutDraftMutationRequestValidatorBase<UpdateCheckoutDraftRequest>
{
    public UpdateCheckoutDraftRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion)
            .GreaterThan(0)
            .WithMessage("Expected version must be greater than zero.")
            .WithErrorCode(CheckoutDraftFieldErrorCodes.ExpectedVersionInvalid);
    }
}
