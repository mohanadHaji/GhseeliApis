using Ghseeli.BusinessApi.DTOs.Availability;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.BusinessApi.Services.Validation.Availability;
using Ghseeli.Common.Logging;

namespace Ghseeli.BusinessApi.Services;

public class AvailabilityManagementService : IAvailabilityManagementService
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IAvailabilityRepository _availabilityRepository;
    private readonly IAvailabilityRequestValidator _requestValidator;
    private readonly IAvailabilityRuleValidator _ruleValidator;
    private readonly ISystemClock _clock;
    private readonly IAppLogger _logger;

    public AvailabilityManagementService(
        ICompanyRepository companyRepository,
        IAvailabilityRepository availabilityRepository,
        IAvailabilityRequestValidator requestValidator,
        IAvailabilityRuleValidator ruleValidator,
        ISystemClock clock,
        IAppLogger logger)
    {
        _companyRepository = companyRepository;
        _availabilityRepository = availabilityRepository;
        _requestValidator = requestValidator;
        _ruleValidator = ruleValidator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<BranchAvailabilitySettingsResponse?> GetSettingsAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId)
    {
        var branch = await ResolveBranchAsync(userId, isAdmin, branchId);
        return branch.AvailabilitySettings is null
            ? null
            : Map(branch.AvailabilitySettings);
    }

    public async Task<BranchAvailabilitySettingsResponse> UpsertSettingsAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId,
        UpdateBranchAvailabilitySettingsRequest request)
    {
        _requestValidator.Validate(request);

        var branch = await ResolveBranchAsync(userId, isAdmin, branchId);
        var utcNow = _clock.UtcNow;
        var settings = branch.AvailabilitySettings;

        if (settings is null)
        {
            settings = new BranchAvailabilitySettings
            {
                BranchId = branch.Id,
                Branch = branch,
                CreatedAt = utcNow
            };
        }

        settings.TimeZoneId = BusinessTextNormalizer.NormalizeRequired(request.TimeZoneId);
        settings.MinimumLeadMinutes = request.MinimumLeadMinutes;
        settings.BookingHorizonDays = request.BookingHorizonDays;
        settings.IsActive = request.IsActive;
        settings.UpdatedAt = utcNow;

        _ruleValidator.ValidateSettings(settings);

        var persisted = branch.AvailabilitySettings is null
            ? await _availabilityRepository.AddSettingsAsync(settings, branch.CompanyId)
            : await _availabilityRepository.UpdateSettingsAsync(settings, branch.CompanyId);

        return Map(persisted);
    }

    public async Task<IReadOnlyCollection<RecurringScheduleResponse>> GetRecurringSchedulesAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId)
    {
        await EnsureBranchAccessAsync(userId, isAdmin, branchId);
        return (await _availabilityRepository.GetRecurringSchedulesByBranchIdAsync(branchId))
            .Select(Map)
            .ToArray();
    }

    public async Task<RecurringScheduleResponse> GetRecurringScheduleAsync(
        Guid userId,
        bool isAdmin,
        Guid scheduleId)
    {
        var schedule = await _availabilityRepository.GetRecurringScheduleByIdAsync(scheduleId)
            ?? throw new KeyNotFoundException("The recurring schedule was not found.");

        await EnsureCompanyAccessAsync(userId, isAdmin, schedule.Branch.CompanyId);
        return Map(schedule);
    }

    public async Task<RecurringScheduleResponse> CreateRecurringScheduleAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId,
        CreateRecurringScheduleRequest request)
    {
        _requestValidator.Validate(request);

        var branch = await ResolveBranchAsync(userId, isAdmin, branchId);
        var schedule = new BranchRecurringSchedule
        {
            BranchId = branch.Id,
            Branch = branch,
            DayOfWeek = request.DayOfWeek,
            StartLocalTime = request.StartLocalTime,
            EndLocalTime = request.EndLocalTime,
            SlotDurationMinutes = request.SlotDurationMinutes,
            Capacity = request.Capacity,
            IsActive = request.IsActive,
            CreatedAt = _clock.UtcNow
        };

        _ruleValidator.ValidateRecurringSchedule(schedule, branch.RecurringSchedules);
        var persisted = await _availabilityRepository.AddRecurringScheduleAsync(
            schedule,
            branch.CompanyId);

        return Map(persisted);
    }

    public async Task<RecurringScheduleResponse> UpdateRecurringScheduleAsync(
        Guid userId,
        bool isAdmin,
        Guid scheduleId,
        UpdateRecurringScheduleRequest request)
    {
        _requestValidator.Validate(request);

        var schedule = await _availabilityRepository.GetRecurringScheduleByIdAsync(scheduleId)
            ?? throw new KeyNotFoundException("The recurring schedule was not found.");

        await EnsureCompanyAccessAsync(userId, isAdmin, schedule.Branch.CompanyId);

        schedule.DayOfWeek = request.DayOfWeek;
        schedule.StartLocalTime = request.StartLocalTime;
        schedule.EndLocalTime = request.EndLocalTime;
        schedule.SlotDurationMinutes = request.SlotDurationMinutes;
        schedule.Capacity = request.Capacity;
        schedule.IsActive = request.IsActive;
        schedule.UpdatedAt = _clock.UtcNow;

        _ruleValidator.ValidateRecurringSchedule(schedule, schedule.Branch.RecurringSchedules);
        var persisted = await _availabilityRepository.UpdateRecurringScheduleAsync(
            schedule,
            schedule.Branch.CompanyId);

        return Map(persisted);
    }

    public async Task DeleteRecurringScheduleAsync(Guid userId, bool isAdmin, Guid scheduleId)
    {
        var schedule = await _availabilityRepository.GetRecurringScheduleByIdAsync(scheduleId)
            ?? throw new KeyNotFoundException("The recurring schedule was not found.");

        await EnsureCompanyAccessAsync(userId, isAdmin, schedule.Branch.CompanyId);
        await _availabilityRepository.DeleteRecurringScheduleAsync(schedule, schedule.Branch.CompanyId);
    }

    public async Task<IReadOnlyCollection<AvailabilityOverrideResponse>> GetAvailabilityOverridesAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        await EnsureBranchAccessAsync(userId, isAdmin, branchId);
        return (await _availabilityRepository.GetAvailabilityOverridesByBranchIdAsync(
                branchId,
                fromDate,
                toDate))
            .Select(Map)
            .ToArray();
    }

    public async Task<AvailabilityOverrideResponse> GetAvailabilityOverrideAsync(
        Guid userId,
        bool isAdmin,
        Guid overrideId)
    {
        var availabilityOverride = await _availabilityRepository.GetAvailabilityOverrideByIdAsync(
                overrideId)
            ?? throw new KeyNotFoundException("The availability override was not found.");

        await EnsureCompanyAccessAsync(userId, isAdmin, availabilityOverride.Branch.CompanyId);
        return Map(availabilityOverride);
    }

    public async Task<AvailabilityOverrideResponse> CreateAvailabilityOverrideAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId,
        CreateAvailabilityOverrideRequest request)
    {
        _requestValidator.Validate(request);

        var branch = await ResolveBranchAsync(userId, isAdmin, branchId);
        var availabilityOverride = new BranchAvailabilityOverride
        {
            BranchId = branch.Id,
            Branch = branch,
            OverrideDate = request.OverrideDate,
            IsClosed = request.IsClosed,
            StartLocalTime = request.StartLocalTime,
            EndLocalTime = request.EndLocalTime,
            SlotDurationMinutes = request.SlotDurationMinutes,
            Capacity = request.Capacity,
            IsActive = request.IsActive,
            CreatedAt = _clock.UtcNow
        };

        _ruleValidator.ValidateAvailabilityOverride(
            availabilityOverride,
            branch.AvailabilityOverrides);

        var persisted = await _availabilityRepository.AddAvailabilityOverrideAsync(
            availabilityOverride,
            branch.CompanyId);

        return Map(persisted);
    }

    public async Task<AvailabilityOverrideResponse> UpdateAvailabilityOverrideAsync(
        Guid userId,
        bool isAdmin,
        Guid overrideId,
        UpdateAvailabilityOverrideRequest request)
    {
        _requestValidator.Validate(request);

        var availabilityOverride = await _availabilityRepository.GetAvailabilityOverrideByIdAsync(
                overrideId)
            ?? throw new KeyNotFoundException("The availability override was not found.");

        await EnsureCompanyAccessAsync(userId, isAdmin, availabilityOverride.Branch.CompanyId);

        availabilityOverride.OverrideDate = request.OverrideDate;
        availabilityOverride.IsClosed = request.IsClosed;
        availabilityOverride.StartLocalTime = request.StartLocalTime;
        availabilityOverride.EndLocalTime = request.EndLocalTime;
        availabilityOverride.SlotDurationMinutes = request.SlotDurationMinutes;
        availabilityOverride.Capacity = request.Capacity;
        availabilityOverride.IsActive = request.IsActive;
        availabilityOverride.UpdatedAt = _clock.UtcNow;

        _ruleValidator.ValidateAvailabilityOverride(
            availabilityOverride,
            availabilityOverride.Branch.AvailabilityOverrides);

        var persisted = await _availabilityRepository.UpdateAvailabilityOverrideAsync(
            availabilityOverride,
            availabilityOverride.Branch.CompanyId);

        return Map(persisted);
    }

    public async Task DeleteAvailabilityOverrideAsync(Guid userId, bool isAdmin, Guid overrideId)
    {
        var availabilityOverride = await _availabilityRepository.GetAvailabilityOverrideByIdAsync(
                overrideId)
            ?? throw new KeyNotFoundException("The availability override was not found.");

        await EnsureCompanyAccessAsync(userId, isAdmin, availabilityOverride.Branch.CompanyId);
        await _availabilityRepository.DeleteAvailabilityOverrideAsync(
            availabilityOverride,
            availabilityOverride.Branch.CompanyId);
    }

    public async Task<BranchServiceAreaResponse?> GetServiceAreaAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId)
    {
        var branch = await ResolveBranchAsync(userId, isAdmin, branchId);
        return branch.ServiceArea is null
            ? null
            : Map(branch.ServiceArea);
    }

    public async Task<BranchServiceAreaResponse> UpsertServiceAreaAsync(
        Guid userId,
        bool isAdmin,
        Guid branchId,
        UpsertBranchServiceAreaRequest request)
    {
        _requestValidator.Validate(request);

        var branch = await ResolveBranchAsync(userId, isAdmin, branchId);
        var utcNow = _clock.UtcNow;
        var serviceArea = branch.ServiceArea;

        if (serviceArea is null)
        {
            serviceArea = new BranchServiceArea
            {
                BranchId = branch.Id,
                Branch = branch,
                CreatedAt = utcNow
            };
        }

        serviceArea.CenterLatitude = request.CenterLatitude;
        serviceArea.CenterLongitude = request.CenterLongitude;
        serviceArea.RadiusKm = request.RadiusKm;
        serviceArea.IsActive = request.IsActive;
        serviceArea.UpdatedAt = utcNow;

        _ruleValidator.ValidateServiceArea(branch, serviceArea);

        var persisted = branch.ServiceArea is null
            ? await _availabilityRepository.AddServiceAreaAsync(serviceArea, branch.CompanyId)
            : await _availabilityRepository.UpdateServiceAreaAsync(serviceArea, branch.CompanyId);

        return Map(persisted);
    }

    public async Task DeleteServiceAreaAsync(Guid userId, bool isAdmin, Guid branchId)
    {
        var branch = await ResolveBranchAsync(userId, isAdmin, branchId);
        if (branch.ServiceArea is null)
        {
            throw new KeyNotFoundException("The service area was not found.");
        }

        await _availabilityRepository.DeleteServiceAreaAsync(branch.ServiceArea, branch.CompanyId);
    }

    private async Task<Branch> ResolveBranchAsync(Guid userId, bool isAdmin, Guid branchId)
    {
        var branch = await _availabilityRepository.GetBranchWithAvailabilityAsync(branchId)
            ?? throw new KeyNotFoundException("The branch was not found.");

        await EnsureCompanyAccessAsync(userId, isAdmin, branch.CompanyId);
        return branch;
    }

    private async Task EnsureBranchAccessAsync(Guid userId, bool isAdmin, Guid branchId)
    {
        var branch = await _availabilityRepository.GetBranchWithAvailabilityAsync(branchId)
            ?? throw new KeyNotFoundException("The branch was not found.");

        await EnsureCompanyAccessAsync(userId, isAdmin, branch.CompanyId);
    }

    private async Task EnsureCompanyAccessAsync(Guid userId, bool isAdmin, Guid companyId)
    {
        if (isAdmin)
        {
            return;
        }

        var company = await _companyRepository.GetForUserAsync(userId);
        if (company?.Id == companyId)
        {
            return;
        }

        _logger.LogWarning(
            $"Availability request rejected because company {companyId} is not assigned to user {userId}.");
        throw new UnauthorizedAccessException(
            "The requested availability resource is not assigned to this business account.");
    }

    private static BranchAvailabilitySettingsResponse Map(BranchAvailabilitySettings settings)
    {
        return new BranchAvailabilitySettingsResponse
        {
            Id = settings.Id,
            BranchId = settings.BranchId,
            TimeZoneId = settings.TimeZoneId,
            MinimumLeadMinutes = settings.MinimumLeadMinutes,
            BookingHorizonDays = settings.BookingHorizonDays,
            IsActive = settings.IsActive
        };
    }

    private static RecurringScheduleResponse Map(BranchRecurringSchedule schedule)
    {
        return new RecurringScheduleResponse
        {
            Id = schedule.Id,
            BranchId = schedule.BranchId,
            DayOfWeek = schedule.DayOfWeek,
            StartLocalTime = schedule.StartLocalTime,
            EndLocalTime = schedule.EndLocalTime,
            SlotDurationMinutes = schedule.SlotDurationMinutes,
            Capacity = schedule.Capacity,
            IsActive = schedule.IsActive
        };
    }

    private static AvailabilityOverrideResponse Map(BranchAvailabilityOverride availabilityOverride)
    {
        return new AvailabilityOverrideResponse
        {
            Id = availabilityOverride.Id,
            BranchId = availabilityOverride.BranchId,
            OverrideDate = availabilityOverride.OverrideDate,
            IsClosed = availabilityOverride.IsClosed,
            StartLocalTime = availabilityOverride.StartLocalTime,
            EndLocalTime = availabilityOverride.EndLocalTime,
            SlotDurationMinutes = availabilityOverride.SlotDurationMinutes,
            Capacity = availabilityOverride.Capacity,
            IsActive = availabilityOverride.IsActive
        };
    }

    private static BranchServiceAreaResponse Map(BranchServiceArea serviceArea)
    {
        return new BranchServiceAreaResponse
        {
            Id = serviceArea.Id,
            BranchId = serviceArea.BranchId,
            CenterLatitude = serviceArea.CenterLatitude,
            CenterLongitude = serviceArea.CenterLongitude,
            RadiusKm = serviceArea.RadiusKm,
            IsActive = serviceArea.IsActive,
            UsesBranchCoordinates = !serviceArea.CenterLatitude.HasValue
        };
    }
}
