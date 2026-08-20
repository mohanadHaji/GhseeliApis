using Ghseeli.BusinessApi.DTOs.Availability;

namespace Ghseeli.BusinessApi.Services.Interfaces;

public interface IAvailabilityManagementService
{
    Task<BranchAvailabilitySettingsResponse?> GetSettingsAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId);
    Task<BranchAvailabilitySettingsResponse> UpsertSettingsAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId,
        UpdateBranchAvailabilitySettingsRequest request);
    Task<IReadOnlyCollection<RecurringScheduleResponse>> GetRecurringSchedulesAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId);
    Task<RecurringScheduleResponse> GetRecurringScheduleAsync(
        Guid userId,
        bool isAdmin,
        Guid scheduleId);
    Task<RecurringScheduleResponse> CreateRecurringScheduleAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId,
        CreateRecurringScheduleRequest request);
    Task<RecurringScheduleResponse> UpdateRecurringScheduleAsync(
        Guid userId,
        bool isAdmin,
        Guid scheduleId,
        UpdateRecurringScheduleRequest request);
    Task DeleteRecurringScheduleAsync(Guid userId, bool isAdmin, Guid scheduleId);
    Task<IReadOnlyCollection<AvailabilityOverrideResponse>> GetAvailabilityOverridesAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId,
        DateOnly? fromDate,
        DateOnly? toDate);
    Task<AvailabilityOverrideResponse> GetAvailabilityOverrideAsync(
        Guid userId,
        bool isAdmin,
        Guid overrideId);
    Task<AvailabilityOverrideResponse> CreateAvailabilityOverrideAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId,
        CreateAvailabilityOverrideRequest request);
    Task<AvailabilityOverrideResponse> UpdateAvailabilityOverrideAsync(
        Guid userId,
        bool isAdmin,
        Guid overrideId,
        UpdateAvailabilityOverrideRequest request);
    Task DeleteAvailabilityOverrideAsync(Guid userId, bool isAdmin, Guid overrideId);
    Task<BranchServiceAreaResponse?> GetServiceAreaAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId);
    Task<BranchServiceAreaResponse> UpsertServiceAreaAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId,
        UpsertBranchServiceAreaRequest request);
    Task DeleteServiceAreaAsync(Guid userId, bool isAdmin, Guid branchId);
}
