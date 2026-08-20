using Ghseeli.BusinessApi.Models;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Services.Availability;

public class TimeZoneAvailabilityResolver : ITimeZoneAvailabilityResolver
{
    private readonly ISystemClock _clock;

    public TimeZoneAvailabilityResolver(ISystemClock clock)
    {
        _clock = clock;
    }

    public AvailabilityResolutionResult Resolve(
        Branch branch,
        DateTime requestedSlotStartUtc,
        int totalDurationMinutes)
    {
        ArgumentNullException.ThrowIfNull(branch);

        var normalizedStartUtc = NormalizeUtc(requestedSlotStartUtc);
        DateTime normalizedEndUtc;
        try
        {
            normalizedEndUtc = normalizedStartUtc.AddMinutes(totalDurationMinutes);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Invalid(
                AppointmentValidationErrorCodes.SlotUnavailable,
                "Requested slot exceeds the supported duration range.",
                new AppointmentAvailabilityFacts
                {
                    RequestedSlotStartUtc = normalizedStartUtc,
                    RequestedSlotEndUtc = normalizedStartUtc,
                    CapacityReservationChecked = false,
                    WindowSource = "None"
                });
        }

        var facts = new AppointmentAvailabilityFacts
        {
            RequestedSlotStartUtc = normalizedStartUtc,
            RequestedSlotEndUtc = normalizedEndUtc,
            CapacityReservationChecked = false,
            WindowSource = "None"
        };

        var settings = branch.AvailabilitySettings;
        if (settings is null)
        {
            return Invalid(
                AppointmentValidationErrorCodes.AvailabilityNotConfigured,
                "Branch availability settings are not configured.",
                facts);
        }

        facts.HasActiveConfiguration = settings.IsActive;
        facts.TimeZoneId = settings.TimeZoneId;

        if (!settings.IsActive)
        {
            return Invalid(
                AppointmentValidationErrorCodes.AvailabilityInactive,
                "Branch availability settings are inactive.",
                facts);
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return Invalid(
                AppointmentValidationErrorCodes.InvalidTimeZone,
                "The branch availability time zone is invalid.",
                facts);
        }
        catch (InvalidTimeZoneException)
        {
            return Invalid(
                AppointmentValidationErrorCodes.InvalidTimeZone,
                "The branch availability time zone is invalid.",
                facts);
        }

        var localStart = AsUnspecified(TimeZoneInfo.ConvertTimeFromUtc(normalizedStartUtc, timeZone));
        var localEnd = AsUnspecified(TimeZoneInfo.ConvertTimeFromUtc(normalizedEndUtc, timeZone));
        var actualDuration = normalizedEndUtc - normalizedStartUtc;

        if (timeZone.IsAmbiguousTime(localStart) ||
            timeZone.IsAmbiguousTime(localEnd) ||
            localEnd - localStart != actualDuration)
        {
            return Invalid(
                AppointmentValidationErrorCodes.SlotUnavailable,
                "Requested slot falls into an unsupported daylight saving transition for the branch time zone.",
                facts);
        }

        var earliestStartUtc = _clock.UtcNow.AddMinutes(settings.MinimumLeadMinutes);
        if (normalizedStartUtc < earliestStartUtc)
        {
            return Invalid(
                AppointmentValidationErrorCodes.SlotBeforeLeadTime,
                "Requested slot does not satisfy the branch minimum lead time.",
                facts);
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, timeZone);
        if (localStart.Date > localNow.Date.AddDays(settings.BookingHorizonDays))
        {
            return Invalid(
                AppointmentValidationErrorCodes.SlotBeyondHorizon,
                "Requested slot exceeds the branch booking horizon.",
                facts);
        }

        var localStartDate = DateOnly.FromDateTime(localStart);
        var activeStartDateOverride = branch.AvailabilityOverrides
            .Where(overrideItem => overrideItem.IsActive)
            .SingleOrDefault(overrideItem => overrideItem.OverrideDate == localStartDate);

        IReadOnlyCollection<WindowOccurrence> windows;
        if (activeStartDateOverride is not null)
        {
            facts.UsedDateOverride = true;

            if (activeStartDateOverride.IsClosed)
            {
                facts.WindowSource = "DateClosure";
                return Invalid(
                    AppointmentValidationErrorCodes.SlotUnavailable,
                    "Requested date is closed by an availability override.",
                    facts);
            }

            windows =
            [
                CreateOccurrence(
                    localStartDate,
                    activeStartDateOverride.StartLocalTime!.Value,
                    activeStartDateOverride.EndLocalTime!.Value,
                    activeStartDateOverride.SlotDurationMinutes!.Value,
                    activeStartDateOverride.Capacity!.Value,
                    "DateOverride",
                    usesDateOverride: true)
            ];
        }
        else
        {
            windows = GetWindowsForStartDate(branch, localStartDate)
                .Concat(GetCarryOverWindows(branch, localStartDate.AddDays(-1)))
                .OrderBy(window => window.StartLocalDateTime)
                .ThenBy(window => window.EndLocalDateTime)
                .ToArray();
        }

        if (windows.Count == 0)
        {
            var hasAnyActiveWindows = branch.RecurringSchedules.Any(schedule => schedule.IsActive)
                || branch.AvailabilityOverrides.Any(overrideItem => overrideItem.IsActive);

            return Invalid(
                hasAnyActiveWindows
                    ? AppointmentValidationErrorCodes.SlotUnavailable
                    : AppointmentValidationErrorCodes.AvailabilityNotConfigured,
                hasAnyActiveWindows
                    ? "Requested slot is outside the branch's effective availability windows."
                    : "Branch availability windows are not configured.",
                facts);
        }

        var misaligned = false;
        foreach (var window in windows)
        {
            if (localStart < window.StartLocalDateTime ||
                localEnd > window.EndLocalDateTime)
            {
                continue;
            }

            facts.UsedDateOverride = window.UsesDateOverride;
            facts.WindowSource = window.Source;
            facts.SlotDurationMinutes = window.SlotDurationMinutes;
            facts.ConfiguredCapacity = window.Capacity;

            var slotDuration = TimeSpan.FromMinutes(window.SlotDurationMinutes);
            var startOffset = localStart - window.StartLocalDateTime;
            var appointmentDuration = localEnd - localStart;

            if (appointmentDuration.Ticks % slotDuration.Ticks != 0 ||
                startOffset.Ticks % slotDuration.Ticks != 0)
            {
                misaligned = true;
                continue;
            }

            facts.IsAvailable = true;
            return new AvailabilityResolutionResult
            {
                IsValid = true,
                Facts = facts
            };
        }

        return Invalid(
            misaligned
                ? AppointmentValidationErrorCodes.SlotMisaligned
                : AppointmentValidationErrorCodes.SlotUnavailable,
            misaligned
                ? "Requested slot does not align to the branch slot boundaries."
                : "Requested slot is outside the branch's effective availability windows.",
            facts);
    }

    private static IReadOnlyCollection<WindowOccurrence> GetWindowsForStartDate(
        Branch branch,
        DateOnly startDate)
    {
        var overrideItem = branch.AvailabilityOverrides
            .Where(item => item.IsActive)
            .SingleOrDefault(item => item.OverrideDate == startDate);

        if (overrideItem is not null)
        {
            if (overrideItem.IsClosed)
            {
                return Array.Empty<WindowOccurrence>();
            }

            return
            [
                CreateOccurrence(
                    startDate,
                    overrideItem.StartLocalTime!.Value,
                    overrideItem.EndLocalTime!.Value,
                    overrideItem.SlotDurationMinutes!.Value,
                    overrideItem.Capacity!.Value,
                    "DateOverride",
                    usesDateOverride: true)
            ];
        }

        return branch.RecurringSchedules
            .Where(schedule => schedule.IsActive)
            .Where(schedule => schedule.DayOfWeek == startDate.DayOfWeek)
            .OrderBy(schedule => schedule.StartLocalTime)
            .Select(schedule => CreateOccurrence(
                startDate,
                schedule.StartLocalTime,
                schedule.EndLocalTime,
                schedule.SlotDurationMinutes,
                schedule.Capacity,
                "RecurringSchedule",
                usesDateOverride: false))
            .ToArray();
    }

    private static IReadOnlyCollection<WindowOccurrence> GetCarryOverWindows(
        Branch branch,
        DateOnly previousDate)
    {
        return GetWindowsForStartDate(branch, previousDate)
            .Where(window => window.CrossesMidnight)
            .ToArray();
    }

    private static WindowOccurrence CreateOccurrence(
        DateOnly anchorDate,
        TimeSpan startLocalTime,
        TimeSpan endLocalTime,
        int slotDurationMinutes,
        int capacity,
        string source,
        bool usesDateOverride)
    {
        var startLocalDateTime = anchorDate.ToDateTime(TimeOnly.MinValue).Add(startLocalTime);
        var endDate = endLocalTime > startLocalTime
            ? anchorDate
            : anchorDate.AddDays(1);
        var endLocalDateTime = endDate.ToDateTime(TimeOnly.MinValue).Add(endLocalTime);

        return new WindowOccurrence(
            startLocalDateTime,
            endLocalDateTime,
            slotDurationMinutes,
            capacity,
            source,
            usesDateOverride,
            CrossesMidnight: endDate != anchorDate);
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static DateTime AsUnspecified(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    private static AvailabilityResolutionResult Invalid(
        string code,
        string message,
        AppointmentAvailabilityFacts facts)
    {
        return new AvailabilityResolutionResult
        {
            IsValid = false,
            Error = new AppointmentValidationIssue
            {
                Code = code,
                Message = message
            },
            Facts = facts
        };
    }

    private sealed record WindowOccurrence(
        DateTime StartLocalDateTime,
        DateTime EndLocalDateTime,
        int SlotDurationMinutes,
        int Capacity,
        string Source,
        bool UsesDateOverride,
        bool CrossesMidnight);
}
