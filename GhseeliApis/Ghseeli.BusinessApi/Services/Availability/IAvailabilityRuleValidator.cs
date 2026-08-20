using Ghseeli.BusinessApi.Models;

namespace Ghseeli.BusinessApi.Services.Availability;

public interface IAvailabilityRuleValidator
{
    void ValidateSettings(BranchAvailabilitySettings settings);
    void ValidateRecurringSchedule(
        BranchRecurringSchedule schedule,
        IEnumerable<BranchRecurringSchedule> existingSchedules);
    void ValidateAvailabilityOverride(
        BranchAvailabilityOverride availabilityOverride,
        IEnumerable<BranchAvailabilityOverride> existingOverrides);
    void ValidateServiceArea(Branch branch, BranchServiceArea serviceArea);
}
