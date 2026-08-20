using FluentValidation;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Availability;
using Ghseeli.BusinessApi.Validators;

namespace Ghseeli.BusinessApi.Validators.Availability;

public class UpdateBranchAvailabilitySettingsRequestValidator
    : AbstractValidator<UpdateBranchAvailabilitySettingsRequest>
{
    public UpdateBranchAvailabilitySettingsRequestValidator()
    {
        RuleFor(request => request.TimeZoneId)
            .RequiredTrimmedText("Time zone id", 100);

        RuleFor(request => request.MinimumLeadMinutes)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Minimum lead minutes cannot be negative.")
            .LessThanOrEqualTo(BusinessValueLimits.MaximumMinimumLeadMinutes)
            .WithMessage($"Minimum lead minutes must be {BusinessValueLimits.MaximumMinimumLeadMinutes} or fewer.");

        RuleFor(request => request.BookingHorizonDays)
            .GreaterThan(0)
            .WithMessage("Booking horizon days must be greater than zero.")
            .LessThanOrEqualTo(BusinessValueLimits.MaximumBookingHorizonDays)
            .WithMessage($"Booking horizon days must be {BusinessValueLimits.MaximumBookingHorizonDays} or fewer.");
    }
}
