using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Services;

namespace Ghseeli.BusinessApi.Services.Availability;

public class AvailabilityRuleValidator : IAvailabilityRuleValidator
{
    private static readonly TimeSpan DaysPerWeek = TimeSpan.FromDays(7);

    public void ValidateSettings(BranchAvailabilitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(settings.TimeZoneId))
        {
            throw AvailabilityValidationException.ForField(
                "timeZoneId",
                "Time zone id is required.");
        }

        if (settings.MinimumLeadMinutes < 0)
        {
            throw AvailabilityValidationException.ForField(
                "minimumLeadMinutes",
                "Minimum lead minutes cannot be negative.");
        }

        if (settings.MinimumLeadMinutes > BusinessValueLimits.MaximumMinimumLeadMinutes)
        {
            throw AvailabilityValidationException.ForField(
                "minimumLeadMinutes",
                $"Minimum lead minutes must be {BusinessValueLimits.MaximumMinimumLeadMinutes} or fewer.");
        }

        if (settings.BookingHorizonDays <= 0)
        {
            throw AvailabilityValidationException.ForField(
                "bookingHorizonDays",
                "Booking horizon days must be greater than zero.");
        }

        if (settings.BookingHorizonDays > BusinessValueLimits.MaximumBookingHorizonDays)
        {
            throw AvailabilityValidationException.ForField(
                "bookingHorizonDays",
                $"Booking horizon days must be {BusinessValueLimits.MaximumBookingHorizonDays} or fewer.");
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw AvailabilityValidationException.ForField(
                "timeZoneId",
                "Time zone id is invalid.");
        }
        catch (InvalidTimeZoneException)
        {
            throw AvailabilityValidationException.ForField(
                "timeZoneId",
                "Time zone id is invalid.");
        }
    }

    public void ValidateRecurringSchedule(
        BranchRecurringSchedule schedule,
        IEnumerable<BranchRecurringSchedule> existingSchedules)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        ValidateWindow(
            schedule.StartLocalTime,
            schedule.EndLocalTime,
            schedule.SlotDurationMinutes,
            schedule.Capacity,
            "startLocalTime",
            "endLocalTime");

        if (!schedule.IsActive)
        {
            return;
        }

        var hasOverlap = existingSchedules
            .Where(existing => existing.Id != schedule.Id)
            .Where(existing => existing.IsActive)
            .Any(existing => Overlaps(schedule, existing));

        if (hasOverlap)
        {
            throw AvailabilityValidationException.ForField(
                "startLocalTime",
                "Recurring schedules cannot overlap for the same branch and day of week.");
        }
    }

    public void ValidateAvailabilityOverride(
        BranchAvailabilityOverride availabilityOverride,
        IEnumerable<BranchAvailabilityOverride> existingOverrides)
    {
        ArgumentNullException.ThrowIfNull(availabilityOverride);

        if (availabilityOverride.IsClosed)
        {
            if (availabilityOverride.StartLocalTime.HasValue ||
                availabilityOverride.EndLocalTime.HasValue ||
                availabilityOverride.SlotDurationMinutes.HasValue ||
                availabilityOverride.Capacity.HasValue)
            {
                throw AvailabilityValidationException.ForField(
                    "isClosed",
                    "Closed date overrides cannot define replacement hours, slot duration, or capacity.");
            }
        }
        else
        {
            if (!availabilityOverride.StartLocalTime.HasValue ||
                !availabilityOverride.EndLocalTime.HasValue ||
                !availabilityOverride.SlotDurationMinutes.HasValue ||
                !availabilityOverride.Capacity.HasValue)
            {
                throw AvailabilityValidationException.ForField(
                    "startLocalTime",
                    "Open date overrides must define replacement hours, slot duration, and capacity.");
            }

            ValidateWindow(
                availabilityOverride.StartLocalTime.Value,
                availabilityOverride.EndLocalTime.Value,
                availabilityOverride.SlotDurationMinutes.Value,
                availabilityOverride.Capacity.Value,
                "startLocalTime",
                "endLocalTime");
        }

        var hasConflict = existingOverrides
            .Where(existing => existing.Id != availabilityOverride.Id)
            .Any(existing => existing.OverrideDate == availabilityOverride.OverrideDate);

        if (hasConflict)
        {
            throw AvailabilityValidationException.ForField(
                "overrideDate",
                "Only one date override can exist for the same branch and date.");
        }
    }

    public void ValidateServiceArea(Branch branch, BranchServiceArea serviceArea)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(serviceArea);

        if (serviceArea.RadiusKm <= 0d)
        {
            throw AvailabilityValidationException.ForField(
                "radiusKm",
                "Radius kilometers must be greater than zero.");
        }

        if (serviceArea.RadiusKm > BusinessValueLimits.MaximumServiceAreaRadiusKm)
        {
            throw AvailabilityValidationException.ForField(
                "radiusKm",
                $"Radius kilometers must be {BusinessValueLimits.MaximumServiceAreaRadiusKm} or fewer.");
        }

        if (serviceArea.CenterLatitude.HasValue != serviceArea.CenterLongitude.HasValue)
        {
            throw AvailabilityValidationException.ForField(
                "centerLatitude",
                "Center latitude and center longitude must be supplied together.");
        }

        if (!serviceArea.CenterLatitude.HasValue &&
            (!branch.Latitude.HasValue || !branch.Longitude.HasValue))
        {
            throw AvailabilityValidationException.ForField(
                "centerLatitude",
                "Branch coordinates are required when the service area does not define an explicit center.");
        }
    }

    private static bool Overlaps(
        BranchRecurringSchedule candidate,
        BranchRecurringSchedule existing)
    {
        var candidateIntervals = GetWeeklyIntervals(candidate);
        var existingIntervals = GetWeeklyIntervals(existing);

        return candidateIntervals.Any(candidateInterval =>
            existingIntervals.Any(existingInterval =>
                candidateInterval.Start < existingInterval.End &&
                existingInterval.Start < candidateInterval.End));
    }

    private static IReadOnlyCollection<WeeklyInterval> GetWeeklyIntervals(
        BranchRecurringSchedule schedule)
    {
        var start = TimeSpan.FromDays((int)schedule.DayOfWeek) + schedule.StartLocalTime;
        var end = start + CalculateWindowDuration(schedule.StartLocalTime, schedule.EndLocalTime);
        return
        [
            new WeeklyInterval(start, end),
            new WeeklyInterval(start + DaysPerWeek, end + DaysPerWeek)
        ];
    }

    private static void ValidateWindow(
        TimeSpan startLocalTime,
        TimeSpan endLocalTime,
        int slotDurationMinutes,
        int capacity,
        string startField,
        string endField)
    {
        if (endLocalTime == startLocalTime)
        {
            throw AvailabilityValidationException.ForField(
                endField,
                "End local time must be different from start local time.");
        }

        if (slotDurationMinutes <= 0)
        {
            throw AvailabilityValidationException.ForField(
                "slotDurationMinutes",
                "Slot duration minutes must be greater than zero.");
        }

        if (slotDurationMinutes > BusinessValueLimits.MaximumSlotDurationMinutes)
        {
            throw AvailabilityValidationException.ForField(
                "slotDurationMinutes",
                $"Slot duration minutes must be {BusinessValueLimits.MaximumSlotDurationMinutes} or fewer.");
        }

        if (capacity <= 0)
        {
            throw AvailabilityValidationException.ForField(
                "capacity",
                "Capacity must be greater than zero.");
        }

        if (capacity > BusinessValueLimits.MaximumConfiguredCapacity)
        {
            throw AvailabilityValidationException.ForField(
                "capacity",
                $"Capacity must be {BusinessValueLimits.MaximumConfiguredCapacity} or fewer.");
        }

        var windowDuration = CalculateWindowDuration(startLocalTime, endLocalTime);
        if (windowDuration.TotalMinutes < slotDurationMinutes)
        {
            throw AvailabilityValidationException.ForField(
                startField,
                "Availability windows must be at least as long as one slot.");
        }

        if (windowDuration.Ticks % TimeSpan.FromMinutes(slotDurationMinutes).Ticks != 0)
        {
            throw AvailabilityValidationException.ForField(
                "slotDurationMinutes",
                "Availability windows must align exactly to the configured slot duration.");
        }
    }

    private static TimeSpan CalculateWindowDuration(
        TimeSpan startLocalTime,
        TimeSpan endLocalTime)
    {
        if (endLocalTime > startLocalTime)
        {
            return endLocalTime - startLocalTime;
        }

        return (TimeSpan.FromDays(1) - startLocalTime) + endLocalTime;
    }

    private sealed record WeeklyInterval(TimeSpan Start, TimeSpan End);
}
