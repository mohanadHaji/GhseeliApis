using FluentValidation;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Validators.Internal;

public class ValidateAppointmentRequestValidator
    : AbstractValidator<ValidateAppointmentRequest>
{
    public ValidateAppointmentRequestValidator()
    {
        RuleFor(request => request.BranchId)
            .NotEmpty()
            .WithMessage("Branch id is required.");

        RuleFor(request => request.OfferingId)
            .NotEmpty()
            .WithMessage("Offering id is required.");

        RuleFor(request => request.RequestedSlotStartUtc)
            .NotEqual(default(DateTimeOffset))
            .WithMessage("Requested slot start UTC is required.");

        RuleFor(request => request.Currency)
            .NotEmpty()
            .WithMessage("Currency is required.")
            .MaximumLength(10)
            .WithMessage("Currency must be 10 characters or fewer.");

        RuleForEach(request => request.SelectedAddons)
            .ChildRules(selection =>
            {
                selection.RuleFor(item => item.AddonChoiceId)
                    .NotEmpty()
                    .WithMessage("Add-on choice id is required.");

                selection.RuleFor(item => item.Quantity)
                    .GreaterThanOrEqualTo(0)
                    .WithMessage("Add-on quantity cannot be negative.")
                    .LessThanOrEqualTo(BusinessValueLimits.MaximumSelectionQuantity)
                    .WithMessage($"Add-on quantity must be {BusinessValueLimits.MaximumSelectionQuantity} or fewer.");
            });

        When(request => request.CustomerLocation is not null, () =>
        {
            RuleFor(request => request.CustomerLocation!.Latitude)
                .InclusiveBetween(-90d, 90d)
                .WithMessage("Customer latitude must be between -90 and 90.");

            RuleFor(request => request.CustomerLocation!.Longitude)
                .InclusiveBetween(-180d, 180d)
                .WithMessage("Customer longitude must be between -180 and 180.");
        });
    }
}
