using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.Common.Logging;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Repositories;

public class AvailabilityRepository : BusinessMutationRepositoryBase, IAvailabilityRepository
{
    private const string ConflictMessage =
        "The requested business availability change conflicted with a newer authoritative update. Reload the latest data and retry.";

    private readonly IAppLogger _logger;

    public AvailabilityRepository(BusinessDbContext context, IAppLogger logger)
        : base(context)
    {
        _logger = logger;
    }

    public Task<Branch?> GetBranchWithAvailabilityAsync(Guid branchId)
    {
        return Context.Branches
            .Include(branch => branch.Company)
            .Include(branch => branch.AvailabilitySettings)
            .Include(branch => branch.RecurringSchedules)
            .Include(branch => branch.AvailabilityOverrides)
            .Include(branch => branch.ServiceArea)
            .SingleOrDefaultAsync(branch => branch.Id == branchId);
    }

    public Task<BranchAvailabilitySettings?> GetSettingsByBranchIdAsync(Guid branchId)
    {
        return Context.BranchAvailabilitySettings
            .Include(settings => settings.Branch)
                .ThenInclude(branch => branch.Company)
            .SingleOrDefaultAsync(settings => settings.BranchId == branchId);
    }

    public async Task<BranchAvailabilitySettings> AddSettingsAsync(
        BranchAvailabilitySettings settings,
        Guid companyId)
    {
        Context.BranchAvailabilitySettings.Add(settings);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: true);
        _logger.LogInfo(
            $"Availability settings created. BranchId={settings.BranchId}, CompanyId={companyId}.");
        return settings;
    }

    public async Task<BranchAvailabilitySettings> UpdateSettingsAsync(
        BranchAvailabilitySettings settings,
        Guid companyId)
    {
        Context.BranchAvailabilitySettings.Update(settings);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: true);
        _logger.LogInfo(
            $"Availability settings updated. BranchId={settings.BranchId}, CompanyId={companyId}.");
        return settings;
    }

    public async Task<IReadOnlyCollection<BranchRecurringSchedule>> GetRecurringSchedulesByBranchIdAsync(
        Guid branchId)
    {
        return await Context.BranchRecurringSchedules
            .AsNoTracking()
            .Where(schedule => schedule.BranchId == branchId)
            .OrderBy(schedule => schedule.DayOfWeek)
            .ThenBy(schedule => schedule.StartLocalTime)
            .ToArrayAsync();
    }

    public Task<BranchRecurringSchedule?> GetRecurringScheduleByIdAsync(Guid scheduleId)
    {
        return Context.BranchRecurringSchedules
            .Include(schedule => schedule.Branch)
                .ThenInclude(branch => branch.Company)
            .Include(schedule => schedule.Branch)
                .ThenInclude(branch => branch.RecurringSchedules)
            .SingleOrDefaultAsync(schedule => schedule.Id == scheduleId);
    }

    public async Task<BranchRecurringSchedule> AddRecurringScheduleAsync(
        BranchRecurringSchedule schedule,
        Guid companyId)
    {
        Context.BranchRecurringSchedules.Add(schedule);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Recurring availability schedule created. ScheduleId={schedule.Id}, BranchId={schedule.BranchId}, CompanyId={companyId}.");
        return schedule;
    }

    public async Task<BranchRecurringSchedule> UpdateRecurringScheduleAsync(
        BranchRecurringSchedule schedule,
        Guid companyId)
    {
        Context.BranchRecurringSchedules.Update(schedule);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Recurring availability schedule updated. ScheduleId={schedule.Id}, BranchId={schedule.BranchId}, CompanyId={companyId}.");
        return schedule;
    }

    public async Task DeleteRecurringScheduleAsync(
        BranchRecurringSchedule schedule,
        Guid companyId)
    {
        Context.BranchRecurringSchedules.Remove(schedule);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Recurring availability schedule deleted. ScheduleId={schedule.Id}, BranchId={schedule.BranchId}, CompanyId={companyId}.");
    }

    public async Task<IReadOnlyCollection<BranchAvailabilityOverride>> GetAvailabilityOverridesByBranchIdAsync(
        Guid branchId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null)
    {
        var query = Context.BranchAvailabilityOverrides
            .AsNoTracking()
            .Where(overrideItem => overrideItem.BranchId == branchId);

        if (fromDate.HasValue)
        {
            query = query.Where(overrideItem => overrideItem.OverrideDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(overrideItem => overrideItem.OverrideDate <= toDate.Value);
        }

        return await query
            .OrderBy(overrideItem => overrideItem.OverrideDate)
            .ToArrayAsync();
    }

    public Task<BranchAvailabilityOverride?> GetAvailabilityOverrideByIdAsync(Guid overrideId)
    {
        return Context.BranchAvailabilityOverrides
            .Include(overrideItem => overrideItem.Branch)
                .ThenInclude(branch => branch.Company)
            .Include(overrideItem => overrideItem.Branch)
                .ThenInclude(branch => branch.AvailabilityOverrides)
            .SingleOrDefaultAsync(overrideItem => overrideItem.Id == overrideId);
    }

    public async Task<BranchAvailabilityOverride> AddAvailabilityOverrideAsync(
        BranchAvailabilityOverride availabilityOverride,
        Guid companyId)
    {
        Context.BranchAvailabilityOverrides.Add(availabilityOverride);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: true);
        _logger.LogInfo(
            $"Availability override created. OverrideId={availabilityOverride.Id}, BranchId={availabilityOverride.BranchId}, CompanyId={companyId}.");
        return availabilityOverride;
    }

    public async Task<BranchAvailabilityOverride> UpdateAvailabilityOverrideAsync(
        BranchAvailabilityOverride availabilityOverride,
        Guid companyId)
    {
        Context.BranchAvailabilityOverrides.Update(availabilityOverride);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: true);
        _logger.LogInfo(
            $"Availability override updated. OverrideId={availabilityOverride.Id}, BranchId={availabilityOverride.BranchId}, CompanyId={companyId}.");
        return availabilityOverride;
    }

    public async Task DeleteAvailabilityOverrideAsync(
        BranchAvailabilityOverride availabilityOverride,
        Guid companyId)
    {
        Context.BranchAvailabilityOverrides.Remove(availabilityOverride);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: true);
        _logger.LogInfo(
            $"Availability override deleted. OverrideId={availabilityOverride.Id}, BranchId={availabilityOverride.BranchId}, CompanyId={companyId}.");
    }

    public Task<BranchServiceArea?> GetServiceAreaByBranchIdAsync(Guid branchId)
    {
        return Context.BranchServiceAreas
            .Include(serviceArea => serviceArea.Branch)
                .ThenInclude(branch => branch.Company)
            .SingleOrDefaultAsync(serviceArea => serviceArea.BranchId == branchId);
    }

    public async Task<BranchServiceArea> AddServiceAreaAsync(
        BranchServiceArea serviceArea,
        Guid companyId)
    {
        Context.BranchServiceAreas.Add(serviceArea);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Service area created. BranchId={serviceArea.BranchId}, CompanyId={companyId}.");
        return serviceArea;
    }

    public async Task<BranchServiceArea> UpdateServiceAreaAsync(
        BranchServiceArea serviceArea,
        Guid companyId)
    {
        Context.BranchServiceAreas.Update(serviceArea);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Service area updated. BranchId={serviceArea.BranchId}, CompanyId={companyId}.");
        return serviceArea;
    }

    public async Task DeleteServiceAreaAsync(BranchServiceArea serviceArea, Guid companyId)
    {
        Context.BranchServiceAreas.Remove(serviceArea);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Service area deleted. BranchId={serviceArea.BranchId}, CompanyId={companyId}.");
    }
}
