using FluentValidation;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Services;

public sealed class AvailabilityDiscoveryService : IAvailabilityDiscoveryService
{
    private readonly BusinessDbContext _context;
    private readonly IServiceAreaCalculator _serviceAreaCalculator;
    private readonly ISystemClock _clock;
    private readonly IValidator<AvailabilityDiscoveryRequest> _validator;

    public AvailabilityDiscoveryService(
        BusinessDbContext context,
        IServiceAreaCalculator serviceAreaCalculator,
        ISystemClock clock,
        IValidator<AvailabilityDiscoveryRequest> validator)
    {
        _context = context;
        _serviceAreaCalculator = serviceAreaCalculator;
        _clock = clock;
        _validator = validator;
    }

    public async Task<AvailabilityDiscoveryResponse> DiscoverAsync(
        AvailabilityDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new AvailabilityValidationException(
                "The availability-discovery request is invalid.",
                validation.Errors
                    .GroupBy(error => ToCamelCase(error.PropertyName))
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage).Distinct().ToArray(),
                        StringComparer.Ordinal));
        }

        var requestedBranchIds = request.Candidates
            .SelectMany(candidate => candidate.BranchIds)
            .Distinct()
            .ToArray();
        var branches = await _context.Branches
            .AsNoTracking()
            .AsSingleQuery()
            .Where(branch => requestedBranchIds.Contains(branch.Id))
            .Include(branch => branch.Company)
            .Include(branch => branch.AvailabilitySettings)
            .Include(branch => branch.RecurringSchedules)
            .Include(branch => branch.AvailabilityOverrides)
            .Include(branch => branch.ServiceArea)
            .ToDictionaryAsync(branch => branch.Id, cancellationToken);

        var results = new List<AvailabilityDiscoveryCompanyResult>();
        foreach (var companyCandidate in request.Candidates)
        {
            BranchSlot? best = null;
            foreach (var branchId in companyCandidate.BranchIds)
            {
                if (!branches.TryGetValue(branchId, out var branch) ||
                    branch.CompanyId != companyCandidate.CompanyId ||
                    !branch.Company.IsActive ||
                    !branch.IsActive ||
                    branch.AvailabilitySettings is null ||
                    !branch.AvailabilitySettings.IsActive)
                {
                    continue;
                }

                if (request.CustomerLocation is not null &&
                    !_serviceAreaCalculator.Evaluate(
                        branch,
                        branch.ServiceArea,
                        request.CustomerLocation).IsValid)
                {
                    continue;
                }

                var candidate = await FindBestSlotAsync(
                    branch,
                    request.Date,
                    request.PreferredLocalTime,
                    cancellationToken);
                if (candidate is not null &&
                    (best is null ||
                     candidate.Distance < best.Distance ||
                     (candidate.Distance == best.Distance &&
                      candidate.StartLocal < best.StartLocal)))
                {
                    best = candidate;
                }
            }

            if (best is not null)
            {
                results.Add(new AvailabilityDiscoveryCompanyResult
                {
                    CompanyId = companyCandidate.CompanyId,
                    BranchId = best.BranchId,
                    TimeZoneId = best.TimeZoneId,
                    SlotStartUtc = best.StartUtc,
                    SlotStartLocal = best.StartLocal,
                    ConfiguredCapacity = best.ConfiguredCapacity,
                    RemainingCapacity = best.RemainingCapacity
                });
            }
        }

        return new AvailabilityDiscoveryResponse
        {
            ContractVersion = Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract.Version,
            Date = request.Date,
            PreferredLocalTime = request.PreferredLocalTime,
            GeneratedAtUtc = _clock.UtcNow,
            Results = results
        };
    }

    private async Task<BranchSlot?> FindBestSlotAsync(
        Branch branch,
        DateOnly date,
        TimeOnly preferredTime,
        CancellationToken cancellationToken)
    {
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                branch.AvailabilitySettings!.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, timeZone);
        if (date < DateOnly.FromDateTime(localNow) ||
            date > DateOnly.FromDateTime(localNow).AddDays(
                branch.AvailabilitySettings.BookingHorizonDays))
        {
            return null;
        }

        var slots = GetWindows(branch, date)
            .SelectMany(window => EnumerateSlots(window, date))
            .Select(slot => ConvertSlot(slot, timeZone))
            .Where(slot => slot is not null)
            .Select(slot => slot!)
            .Where(slot => slot.StartUtc >=
                _clock.UtcNow.AddMinutes(branch.AvailabilitySettings.MinimumLeadMinutes))
            .OrderBy(slot => slot.StartUtc)
            .ToArray();
        if (slots.Length == 0)
        {
            return null;
        }

        var rangeStart = slots.Min(slot => slot.StartUtc);
        var rangeEnd = slots.Max(slot => slot.StartUtc.AddMinutes(slot.SlotDurationMinutes));
        var reservations = await _context.AppointmentReservations
            .AsNoTracking()
            .Where(reservation =>
                reservation.BranchId == branch.Id &&
                BookingStatuses.CapacityOccupying.Contains(reservation.Status) &&
                reservation.RequestedSlotStartUtc < rangeEnd &&
                reservation.RequestedSlotEndUtc > rangeStart)
            .Select(reservation => new
            {
                reservation.RequestedSlotStartUtc,
                reservation.RequestedSlotEndUtc
            })
            .ToArrayAsync(cancellationToken);

        var preferred = date.ToDateTime(preferredTime, DateTimeKind.Unspecified);
        return slots
            .Select(slot =>
            {
                var endUtc = slot.StartUtc.AddMinutes(slot.SlotDurationMinutes);
                var occupied = reservations.Count(reservation =>
                    reservation.RequestedSlotStartUtc < endUtc &&
                    reservation.RequestedSlotEndUtc > slot.StartUtc);
                return new BranchSlot(
                    branch.Id,
                    branch.AvailabilitySettings.TimeZoneId,
                    slot.StartLocal,
                    slot.StartUtc,
                    slot.Capacity,
                    Math.Max(0, slot.Capacity - occupied),
                    (slot.StartLocal - preferred).Duration());
            })
            .Where(slot => slot.RemainingCapacity > 0)
            .OrderBy(slot => slot.Distance)
            .ThenBy(slot => slot.StartLocal)
            .FirstOrDefault();
    }

    private static IReadOnlyCollection<Window> GetWindows(Branch branch, DateOnly date)
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
                    overrideItem.SlotDurationMinutes!.Value,
                    overrideItem.Capacity!.Value)
            ];
        }

        var windows = branch.RecurringSchedules
            .Where(schedule => schedule.IsActive && schedule.DayOfWeek == date.DayOfWeek)
            .OrderBy(schedule => schedule.StartLocalTime)
            .Select(schedule => CreateWindow(
                date,
                schedule.StartLocalTime,
                schedule.EndLocalTime,
                schedule.SlotDurationMinutes,
                schedule.Capacity))
            .ToList();
        windows.AddRange(branch.RecurringSchedules
            .Where(schedule =>
                schedule.IsActive &&
                schedule.DayOfWeek == date.AddDays(-1).DayOfWeek &&
                schedule.EndLocalTime <= schedule.StartLocalTime)
            .OrderBy(schedule => schedule.StartLocalTime)
            .Select(schedule => CreateWindow(
                date.AddDays(-1),
                schedule.StartLocalTime,
                schedule.EndLocalTime,
                schedule.SlotDurationMinutes,
                schedule.Capacity)));
        return windows;
    }

    private static Window CreateWindow(
        DateOnly date,
        TimeSpan start,
        TimeSpan end,
        int duration,
        int capacity)
    {
        var startLocal = date.ToDateTime(TimeOnly.MinValue).Add(start);
        var endDate = end > start ? date : date.AddDays(1);
        return new Window(
            startLocal,
            endDate.ToDateTime(TimeOnly.MinValue).Add(end),
            duration,
            capacity);
    }

    private static IEnumerable<Slot> EnumerateSlots(Window window, DateOnly date)
    {
        for (var start = window.StartLocal;
             start < window.EndLocal;
             start = start.AddMinutes(window.SlotDurationMinutes))
        {
            if (DateOnly.FromDateTime(start) == date)
            {
                yield return new Slot(
                    DateTime.SpecifyKind(start, DateTimeKind.Unspecified),
                    default,
                    window.SlotDurationMinutes,
                    window.Capacity);
            }

        }
    }

    private static Slot? ConvertSlot(Slot slot, TimeZoneInfo timeZone)
    {
        var endLocal = slot.StartLocal.AddMinutes(slot.SlotDurationMinutes);
        if (timeZone.IsInvalidTime(slot.StartLocal) ||
            timeZone.IsAmbiguousTime(slot.StartLocal) ||
            timeZone.IsInvalidTime(endLocal) ||
            timeZone.IsAmbiguousTime(endLocal))
        {
            return null;
        }

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(slot.StartLocal, timeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone);
        return endUtc - startUtc == TimeSpan.FromMinutes(slot.SlotDurationMinutes)
            ? slot with { StartUtc = startUtc }
            : null;
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];

    private sealed record Window(
        DateTime StartLocal,
        DateTime EndLocal,
        int SlotDurationMinutes,
        int Capacity);

    private sealed record Slot(
        DateTime StartLocal,
        DateTime StartUtc,
        int SlotDurationMinutes,
        int Capacity);

    private sealed record BranchSlot(
        Guid BranchId,
        string TimeZoneId,
        DateTime StartLocal,
        DateTime StartUtc,
        int ConfiguredCapacity,
        int RemainingCapacity,
        TimeSpan Distance);
}
