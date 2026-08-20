using FluentValidation;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Availability;

namespace Ghseeli.BusinessApi.Validators.Availability;

public class CreateRecurringScheduleRequestValidator
    : AbstractValidator<CreateRecurringScheduleRequest>
{
    public CreateRecurringScheduleRequestValidator()
    {
        RuleFor(request => request.DayOfWeek)
            .IsInEnum()
            .WithMessage("Day of week is invalid.");

        RuleFor(request => request.SlotDurationMinutes)
            .GreaterThan(0)
            .WithMessage("Slot duration minutes must be greater than zero.")
            .LessThanOrEqualTo(BusinessValueLimits.MaximumSlotDurationMinutes)
            .WithMessage($"Slot duration minutes must be {BusinessValueLimits.MaximumSlotDurationMinutes} or fewer.");

        RuleFor(request => request.Capacity)
            .GreaterThan(0)
            .WithMessage("Capacity must be greater than zero.")
            .LessThanOrEqualTo(BusinessValueLimits.MaximumConfiguredCapacity)
            .WithMessage($"Capacity must be {BusinessValueLimits.MaximumConfiguredCapacity} or fewer.");
    }
}
