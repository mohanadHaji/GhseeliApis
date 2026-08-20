using FluentValidation;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Availability;

namespace Ghseeli.BusinessApi.Validators.Availability;

public class UpsertBranchServiceAreaRequestValidator
    : AbstractValidator<UpsertBranchServiceAreaRequest>
{
    public UpsertBranchServiceAreaRequestValidator()
    {
        RuleFor(request => request.CenterLatitude)
            .Must(value => !value.HasValue || (value.Value >= -90d && value.Value <= 90d))
            .WithMessage("Center latitude must be between -90 and 90.");

        RuleFor(request => request.CenterLongitude)
            .Must(value => !value.HasValue || (value.Value >= -180d && value.Value <= 180d))
            .WithMessage("Center longitude must be between -180 and 180.");

        RuleFor(request => request)
            .Must(request => request.CenterLatitude.HasValue == request.CenterLongitude.HasValue)
            .WithMessage("Center latitude and center longitude must be supplied together.");

        RuleFor(request => request.RadiusKm)
            .GreaterThan(0d)
            .WithMessage("Radius kilometers must be greater than zero.")
            .LessThanOrEqualTo(BusinessValueLimits.MaximumServiceAreaRadiusKm)
            .WithMessage($"Radius kilometers must be {BusinessValueLimits.MaximumServiceAreaRadiusKm} or fewer.");
    }
}
