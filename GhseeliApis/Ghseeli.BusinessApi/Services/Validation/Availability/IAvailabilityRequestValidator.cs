using Ghseeli.BusinessApi.DTOs.Availability;

namespace Ghseeli.BusinessApi.Services.Validation.Availability;

public interface IAvailabilityRequestValidator
{
    void Validate(UpdateBranchAvailabilitySettingsRequest request);
    void Validate(CreateRecurringScheduleRequest request);
    void Validate(UpdateRecurringScheduleRequest request);
    void Validate(CreateAvailabilityOverrideRequest request);
    void Validate(UpdateAvailabilityOverrideRequest request);
    void Validate(UpsertBranchServiceAreaRequest request);
}
