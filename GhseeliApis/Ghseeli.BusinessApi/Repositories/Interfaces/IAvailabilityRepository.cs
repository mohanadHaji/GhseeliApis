using Ghseeli.BusinessApi.Models;

namespace Ghseeli.BusinessApi.Repositories.Interfaces;

public interface IAvailabilityRepository
{
    Task<Branch?> GetBranchWithAvailabilityAsync(Guid branchId);
    Task<BranchAvailabilitySettings?> GetSettingsByBranchIdAsync(Guid branchId);
    Task<BranchAvailabilitySettings> AddSettingsAsync(
        BranchAvailabilitySettings settings,
        Guid companyId);
    Task<BranchAvailabilitySettings> UpdateSettingsAsync(
        BranchAvailabilitySettings settings,
        Guid companyId);
    Task<IReadOnlyCollection<BranchRecurringSchedule>> GetRecurringSchedulesByBranchIdAsync(
        Guid branchId);
    Task<BranchRecurringSchedule?> GetRecurringScheduleByIdAsync(Guid scheduleId);
    Task<BranchRecurringSchedule> AddRecurringScheduleAsync(
        BranchRecurringSchedule schedule,
        Guid companyId);
    Task<BranchRecurringSchedule> UpdateRecurringScheduleAsync(
        BranchRecurringSchedule schedule,
        Guid companyId);
    Task DeleteRecurringScheduleAsync(BranchRecurringSchedule schedule, Guid companyId);
    Task<IReadOnlyCollection<BranchAvailabilityOverride>> GetAvailabilityOverridesByBranchIdAsync(
        Guid branchId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null);
    Task<BranchAvailabilityOverride?> GetAvailabilityOverrideByIdAsync(Guid overrideId);
    Task<BranchAvailabilityOverride> AddAvailabilityOverrideAsync(
        BranchAvailabilityOverride availabilityOverride,
        Guid companyId);
    Task<BranchAvailabilityOverride> UpdateAvailabilityOverrideAsync(
        BranchAvailabilityOverride availabilityOverride,
        Guid companyId);
    Task DeleteAvailabilityOverrideAsync(
        BranchAvailabilityOverride availabilityOverride,
        Guid companyId);
    Task<BranchServiceArea?> GetServiceAreaByBranchIdAsync(Guid branchId);
    Task<BranchServiceArea> AddServiceAreaAsync(BranchServiceArea serviceArea, Guid companyId);
    Task<BranchServiceArea> UpdateServiceAreaAsync(BranchServiceArea serviceArea, Guid companyId);
    Task DeleteServiceAreaAsync(BranchServiceArea serviceArea, Guid companyId);
}
