using FluentValidation;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Availability;

namespace Ghseeli.BusinessApi.Validators.Availability;

public class CreateAvailabilityOverrideRequestValidator
    : AbstractValidator<CreateAvailabilityOverrideRequest>
{
    public CreateAvailabilityOverrideRequestValidator()
    {
        RuleFor(request => request.OverrideDate)
            .NotEqual(default(DateOnly))
            .WithMessage("Override date is required.");

        RuleFor(request => request.SlotDurationMinutes)
            .Must(value => !value.HasValue || (value.Value > 0 &&
                value.Value <= BusinessValueLimits.MaximumSlotDurationMinutes))
            .WithMessage(
                $"Slot duration minutes must be between 1 and {BusinessValueLimits.MaximumSlotDurationMinutes} when supplied.");

        RuleFor(request => request.Capacity)
            .Must(value => !value.HasValue || (value.Value > 0 &&
                value.Value <= BusinessValueLimits.MaximumConfiguredCapacity))
            .WithMessage(
                $"Capacity must be between 1 and {BusinessValueLimits.MaximumConfiguredCapacity} when supplied.");
    }
}
