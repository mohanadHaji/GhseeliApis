using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Services;

public sealed class AvailableSlotsService : IAvailableSlotsService
{
    private static readonly HashSet<string> AvailabilityOnlyErrors =
    [
        AppointmentValidationErrorCodes.AvailabilityNotConfigured,
        AppointmentValidationErrorCodes.AvailabilityInactive,
        AppointmentValidationErrorCodes.InvalidTimeZone,
        AppointmentValidationErrorCodes.SlotBeforeLeadTime,
        AppointmentValidationErrorCodes.SlotBeyondHorizon,
        AppointmentValidationErrorCodes.SlotUnavailable,
        AppointmentValidationErrorCodes.SlotMisaligned
    ];

    private readonly BusinessDbContext _context;
    private readonly IAvailabilityRepository _availabilityRepository;
    private readonly IAppointmentValidationService _appointmentValidationService;
    private readonly ITimeZoneAvailabilityResolver _availabilityResolver;
    private readonly ISystemClock _clock;
    private readonly IValidator<AvailableSlotsRequest> _validator;

    public AvailableSlotsService(
        BusinessDbContext context,
        IAvailabilityRepository availabilityRepository,
        IAppointmentValidationService appointmentValidationService,
        ITimeZoneAvailabilityResolver availabilityResolver,
        ISystemClock clock,
        IValidator<AvailableSlotsRequest> validator)
    {
        _context = context;
        _availabilityRepository = availabilityRepository;
        _appointmentValidationService = appointmentValidationService;
        _availabilityResolver = availabilityResolver;
        _clock = clock;
        _validator = validator;
    }

    public async Task<AvailableSlotsResponse> GetAsync(
        AvailableSlotsRequest request,
        CancellationToken cancellationToken)
    {
        var requestValidation = await _validator.ValidateAsync(request, cancellationToken);
        if (!requestValidation.IsValid)
        {
            throw new AvailabilityValidationException(
                requestValidation.Errors[0].ErrorMessage,
                requestValidation.Errors
                    .GroupBy(error => ToCamelCase(error.PropertyName))
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                        StringComparer.Ordinal));
        }

        var response = CreateResponse(request);
        var branch = await _availabilityRepository.GetBranchWithAvailabilityAsync(
            request.BranchId);
        if (branch is null || branch.CompanyId != request.CompanyId)
        {
            response.Errors =
            [
                Issue(
                    AppointmentValidationErrorCodes.BranchNotFound,
                    "The requested branch was not found.",
                    "branchId")
            ];
            return response;
        }

        response.CatalogVersion = branch.Company.CatalogVersion;
        response.TimeZoneId = branch.AvailabilitySettings?.TimeZoneId;
        if (request.Items.GroupBy(item => item.OfferingId).Any(group => group.Count() > 1))
        {
            response.Errors =
            [
                Issue(
                    AppointmentValidationErrorCodes.AddonSelectionRuleViolation,
                    "Each offering can be supplied only once.",
                    "items")
            ];
            return response;
        }

        var probeStart = CreateProbeStart(request.Date, branch);
        var issues = new List<AppointmentValidationIssue>();
        var totalDuration = 0;
        foreach (var item in request.Items)
        {
            var validation = await _appointmentValidationService.ValidateAsync(
                new ValidateAppointmentRequest
                {
                    BranchId = request.BranchId,
                    OfferingId = item.OfferingId,
                    SelectedAddons = item.SelectedAddons,
                    RequestedSlotStartUtc = probeStart,
                    CustomerLocation = request.CustomerLocation,
                    ExpectedCatalogVersion = request.ExpectedCatalogVersion,
                    Currency = request.Currency
                });
            response.CatalogVersion = validation.CatalogVersion;
            response.Currency = validation.Currency;
            issues.AddRange(validation.Errors.Where(error =>
                !AvailabilityOnlyErrors.Contains(error.Code)));
            try
            {
                totalDuration = checked(totalDuration + validation.TotalDurationMinutes);
            }
            catch (OverflowException)
            {
                issues.Add(Issue(
                    AppointmentValidationErrorCodes.AddonSelectionRuleViolation,
                    "The requested appointment duration exceeds supported limits.",
                    "items"));
            }
        }

        response.TotalDurationMinutes = totalDuration;
        if (totalDuration <= 0 && issues.Count == 0)
        {
            issues.Add(Issue(
                AppointmentValidationErrorCodes.AddonSelectionRuleViolation,
                "The authoritative appointment duration must be positive.",
                "items"));
        }
        response.Errors = issues
            .GroupBy(issue => new { issue.Code, issue.Message, issue.Field })
            .Select(group => group.First())
            .ToArray();
        if (response.Errors.Count > 0 || totalDuration <= 0)
        {
            return response;
        }

        if (branch.AvailabilitySettings is null ||
            !branch.AvailabilitySettings.IsActive)
        {
            response.Valid = true;
            return response;
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                branch.AvailabilitySettings.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            response.Errors =
            [
                Issue(
                    AppointmentValidationErrorCodes.InvalidTimeZone,
                    "The branch availability time zone is invalid.")
            ];
            return response;
        }
        catch (InvalidTimeZoneException)
        {
            response.Errors =
            [
                Issue(
                    AppointmentValidationErrorCodes.InvalidTimeZone,
                    "The branch availability time zone is invalid.")
            ];
            return response;
        }

        var candidates = GetWindows(branch, request.Date)
            .SelectMany(window => EnumerateCandidates(window, request.Date))
            .Where(local => !timeZone.IsInvalidTime(local) && !timeZone.IsAmbiguousTime(local))
            .Select(local => new
            {
                Local = local,
                StartUtc = TimeZoneInfo.ConvertTimeToUtc(local, timeZone)
            })
            .Select(candidate => new
            {
                candidate.Local,
                candidate.StartUtc,
                Resolution = _availabilityResolver.Resolve(
                    branch,
                    candidate.StartUtc,
                    totalDuration)
            })
            .Where(candidate => candidate.Resolution.IsValid)
            .GroupBy(candidate => candidate.StartUtc)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.StartUtc)
            .ToArray();

        if (candidates.Length == 0)
        {
            response.Valid = true;
            return response;
        }

        var rangeStart = candidates.Min(candidate => candidate.StartUtc);
        var rangeEnd = candidates.Max(candidate =>
            candidate.Resolution.Facts.RequestedSlotEndUtc);
        var reservations = await _context.AppointmentReservations
            .AsNoTracking()
            .Where(reservation =>
                reservation.BranchId == request.BranchId &&
                BookingStatuses.CapacityOccupying.Contains(reservation.Status) &&
                reservation.RequestedSlotStartUtc < rangeEnd &&
                reservation.RequestedSlotEndUtc > rangeStart)
            .Select(reservation => new
            {
                reservation.RequestedSlotStartUtc,
                reservation.RequestedSlotEndUtc
            })
            .ToArrayAsync(cancellationToken);

        response.Slots = candidates
            .Select(candidate =>
            {
                var facts = candidate.Resolution.Facts;
                var configuredCapacity = facts.ConfiguredCapacity ?? 0;
                var occupied = reservations.Count(reservation =>
                    reservation.RequestedSlotStartUtc < facts.RequestedSlotEndUtc &&
                    reservation.RequestedSlotEndUtc > facts.RequestedSlotStartUtc);
                var remaining = Math.Max(0, configuredCapacity - occupied);
                return new AvailableSlotResponse
                {
                    StartUtc = facts.RequestedSlotStartUtc,
                    EndUtc = facts.RequestedSlotEndUtc,
                    StartLocal = candidate.Local,
                    EndLocal = TimeZoneInfo.ConvertTimeFromUtc(
                        facts.RequestedSlotEndUtc,
                        timeZone),
                    ConfiguredCapacity = configuredCapacity,
                    RemainingCapacity = remaining,
                    IsAvailable = remaining > 0
                };
            })
            .Where(slot => request.IncludeUnavailable || slot.IsAvailable)
            .ToArray();
        response.Valid = true;
        return response;
    }

    private AvailableSlotsResponse CreateResponse(AvailableSlotsRequest request) => new()
    {
        ContractVersion = Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract.Version,
        CompanyId = request.CompanyId,
        BranchId = request.BranchId,
        Date = request.Date,
        Currency = request.Currency,
        GeneratedAtUtc = _clock.UtcNow
    };

    private static DateTimeOffset CreateProbeStart(DateOnly date, Branch branch)
    {
        var local = date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                branch.AvailabilitySettings?.TimeZoneId ?? "UTC");
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone));
        }
        catch (TimeZoneNotFoundException)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc));
        }
        catch (InvalidTimeZoneException)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc));
        }
    }

    private static IEnumerable<Window> GetWindows(Branch branch, DateOnly date)
    {
        foreach (var window in GetWindowsForDate(branch, date))
        {
            yield return window;
        }

        foreach (var window in GetWindowsForDate(branch, date.AddDays(-1))
                     .Where(window => window.EndLocal.Date > window.StartLocal.Date))
        {
            yield return window;
        }
    }

    private static IReadOnlyCollection<Window> GetWindowsForDate(
        Branch branch,
        DateOnly date)
    {
        var overrideItem = branch.AvailabilityOverrides
            .Where(item => item.IsActive)
            .SingleOrDefault(item => item.OverrideDate == date);
        if (overrideItem is not null)
        {
            if (overrideItem.IsClosed)
            {
                return Array.Empty<Window>();
            }

            return
            [
                CreateWindow(
                    date,
                    overrideItem.StartLocalTime!.Value,
                    overrideItem.EndLocalTime!.Value,
                    overrideItem.SlotDurationMinutes!.Value)
            ];
        }

        return branch.RecurringSchedules
            .Where(schedule => schedule.IsActive && schedule.DayOfWeek == date.DayOfWeek)
            .OrderBy(schedule => schedule.StartLocalTime)
            .Select(schedule => CreateWindow(
                date,
                schedule.StartLocalTime,
                schedule.EndLocalTime,
                schedule.SlotDurationMinutes))
            .ToArray();
    }

    private static Window CreateWindow(
        DateOnly date,
        TimeSpan start,
        TimeSpan end,
        int slotDurationMinutes)
    {
        var startLocal = date.ToDateTime(TimeOnly.MinValue).Add(start);
        var endDate = end > start ? date : date.AddDays(1);
        return new Window(
            startLocal,
            endDate.ToDateTime(TimeOnly.MinValue).Add(end),
            slotDurationMinutes);
    }

    private static IEnumerable<DateTime> EnumerateCandidates(
        Window window,
        DateOnly requestedDate)
    {
        if (window.SlotDurationMinutes <= 0)
        {
            yield break;
        }

        for (var candidate = window.StartLocal;
             candidate < window.EndLocal;
             candidate = candidate.AddMinutes(window.SlotDurationMinutes))
        {
            if (DateOnly.FromDateTime(candidate) == requestedDate)
            {
                yield return DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified);
            }
        }
    }

    private static AppointmentValidationIssue Issue(
        string code,
        string message,
        string? field = null) => new()
    {
        Code = code,
        Message = message,
        Field = field
    };

    private static string ToCamelCase(string propertyName) =>
        string.IsNullOrWhiteSpace(propertyName)
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];

    private sealed record Window(
        DateTime StartLocal,
        DateTime EndLocal,
        int SlotDurationMinutes);
}
