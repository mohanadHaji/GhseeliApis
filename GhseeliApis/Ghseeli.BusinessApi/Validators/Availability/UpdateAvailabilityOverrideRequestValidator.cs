using FluentValidation;
using Ghseeli.BusinessApi.DTOs.Availability;

namespace Ghseeli.BusinessApi.Validators.Availability;

public class UpdateAvailabilityOverrideRequestValidator
    : AbstractValidator<UpdateAvailabilityOverrideRequest>
{
    public UpdateAvailabilityOverrideRequestValidator()
    {
        Include(new CreateAvailabilityOverrideRequestValidator());
    }
}
