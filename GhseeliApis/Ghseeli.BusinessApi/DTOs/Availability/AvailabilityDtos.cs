namespace Ghseeli.BusinessApi.DTOs.Availability;

public class UpdateBranchAvailabilitySettingsRequest
{
    public string TimeZoneId { get; set; } = string.Empty;
    public int MinimumLeadMinutes { get; set; }
    public int BookingHorizonDays { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BranchAvailabilitySettingsResponse
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public int MinimumLeadMinutes { get; set; }
    public int BookingHorizonDays { get; set; }
    public bool IsActive { get; set; }
}

public class CreateRecurringScheduleRequest
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartLocalTime { get; set; }
    public TimeSpan EndLocalTime { get; set; }
    public int SlotDurationMinutes { get; set; }
    public int Capacity { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateRecurringScheduleRequest : CreateRecurringScheduleRequest;

public class RecurringScheduleResponse
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartLocalTime { get; set; }
    public TimeSpan EndLocalTime { get; set; }
    public int SlotDurationMinutes { get; set; }
    public int Capacity { get; set; }
    public bool IsActive { get; set; }
}

public class CreateAvailabilityOverrideRequest
{
    public DateOnly OverrideDate { get; set; }
    public bool IsClosed { get; set; }
    public TimeSpan? StartLocalTime { get; set; }
    public TimeSpan? EndLocalTime { get; set; }
    public int? SlotDurationMinutes { get; set; }
    public int? Capacity { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateAvailabilityOverrideRequest : CreateAvailabilityOverrideRequest;

public class AvailabilityOverrideResponse
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly OverrideDate { get; set; }
    public bool IsClosed { get; set; }
    public TimeSpan? StartLocalTime { get; set; }
    public TimeSpan? EndLocalTime { get; set; }
    public int? SlotDurationMinutes { get; set; }
    public int? Capacity { get; set; }
    public bool IsActive { get; set; }
}

public class UpsertBranchServiceAreaRequest
{
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double RadiusKm { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BranchServiceAreaResponse
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double RadiusKm { get; set; }
    public bool IsActive { get; set; }
    public bool UsesBranchCoordinates { get; set; }
}
