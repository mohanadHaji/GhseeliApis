using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies UTC-to-local slot resolution, lead-time, overrides, and slot-boundary enforcement.
/// </summary>
public class TimeZoneAvailabilityResolverTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc));
    private readonly TimeZoneAvailabilityResolver _resolver;

    public TimeZoneAvailabilityResolverTests()
    {
        _resolver = new TimeZoneAvailabilityResolver(_clock);
    }

    [Fact]
    public void Resolve_WhenRecurringWindowContainsAlignedSlot_ReturnsAvailable()
    {
        var branch = CreateBranch();

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 8, 24, 9, 30, 0, DateTimeKind.Utc),
            30);

        result.IsValid.Should().BeTrue();
        result.Facts.IsAvailable.Should().BeTrue();
        result.Facts.SlotDurationMinutes.Should().Be(15);
        result.Facts.ConfiguredCapacity.Should().Be(4);
    }

    [Fact]
    public void Resolve_WhenSlotViolatesLeadTime_ReturnsStableError()
    {
        var branch = CreateBranch(minimumLeadMinutes: 90);

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc),
            30);

        result.IsValid.Should().BeFalse();
        result.Error!.Code.Should().Be(AppointmentValidationErrorCodes.SlotBeforeLeadTime);
    }

    [Fact]
    public void Resolve_WhenLeadTimeBoundaryExactlyMet_ReturnsAvailable()
    {
        var branch = CreateBranch(minimumLeadMinutes: 90);

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc),
            30);

        result.IsValid.Should().BeTrue();
        result.Facts.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Resolve_WhenBookingHorizonBoundaryExactlyMet_ReturnsAvailable()
    {
        var branch = CreateBranch(bookingHorizonDays: 7);

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc),
            30);

        result.IsValid.Should().BeTrue();
        result.Facts.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Resolve_WhenDateOverrideClosesDay_ReturnsUnavailable()
    {
        var branch = CreateBranch();
        branch.AvailabilityOverrides.Add(new BranchAvailabilityOverride
        {
            BranchId = branch.Id,
            OverrideDate = new DateOnly(2026, 8, 24),
            IsClosed = true,
            IsActive = true
        });

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc),
            30);

        result.IsValid.Should().BeFalse();
        result.Error!.Code.Should().Be(AppointmentValidationErrorCodes.SlotUnavailable);
        result.Facts.UsedDateOverride.Should().BeTrue();
        result.Facts.WindowSource.Should().Be("DateClosure");
    }

    [Fact]
    public void Resolve_WhenInactiveOverrideExists_UsesRecurringSchedule()
    {
        var branch = CreateBranch();
        branch.AvailabilityOverrides.Add(new BranchAvailabilityOverride
        {
            BranchId = branch.Id,
            OverrideDate = new DateOnly(2026, 8, 24),
            IsClosed = true,
            IsActive = false
        });

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc),
            30);

        result.IsValid.Should().BeTrue();
        result.Facts.UsedDateOverride.Should().BeFalse();
        result.Facts.WindowSource.Should().Be("RecurringSchedule");
    }

    [Fact]
    public void Resolve_WhenDurationDoesNotAlignToSlotBoundary_ReturnsSlotMisaligned()
    {
        var branch = CreateBranch();

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc),
            20);

        result.IsValid.Should().BeFalse();
        result.Error!.Code.Should().Be(AppointmentValidationErrorCodes.SlotMisaligned);
    }

    [Fact]
    public void Resolve_WhenSettingsAreInactive_ReturnsAvailabilityInactive()
    {
        var branch = CreateBranch(settingsActive: false);

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc),
            30);

        result.IsValid.Should().BeFalse();
        result.Error!.Code.Should().Be(AppointmentValidationErrorCodes.AvailabilityInactive);
    }

    [Fact]
    public void Resolve_WhenOvernightScheduleCoversPostMidnightStart_ReturnsAvailable()
    {
        var branch = CreateBranch(
            dayOfWeek: DayOfWeek.Monday,
            startLocalTime: TimeSpan.FromHours(22),
            endLocalTime: TimeSpan.FromHours(2));

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 8, 18, 0, 30, 0, DateTimeKind.Utc),
            30);

        result.IsValid.Should().BeTrue();
        result.Facts.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Resolve_WhenSlotEndsExactlyAtOvernightBoundary_ReturnsAvailable()
    {
        var branch = CreateBranch(
            dayOfWeek: DayOfWeek.Monday,
            startLocalTime: TimeSpan.FromHours(22),
            endLocalTime: TimeSpan.FromHours(2));

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 8, 18, 1, 30, 0, DateTimeKind.Utc),
            30);

        result.IsValid.Should().BeTrue();
        result.Facts.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Resolve_WhenSlotCrossesOvernightBoundaryEnd_ReturnsUnavailable()
    {
        var branch = CreateBranch(
            dayOfWeek: DayOfWeek.Monday,
            startLocalTime: TimeSpan.FromHours(22),
            endLocalTime: TimeSpan.FromHours(2));

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 8, 18, 1, 30, 0, DateTimeKind.Utc),
            60);

        result.IsValid.Should().BeFalse();
        result.Error!.Code.Should().Be(AppointmentValidationErrorCodes.SlotUnavailable);
    }

    [Fact]
    public void Resolve_WhenSlotCrossesSpringForwardGap_ReturnsUnavailable()
    {
        var branch = CreateBranch(
            timeZoneId: "GMT Standard Time",
            dayOfWeek: DayOfWeek.Sunday,
            startLocalTime: TimeSpan.Zero,
            endLocalTime: TimeSpan.FromHours(4));

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 3, 29, 0, 30, 0, DateTimeKind.Utc),
            60);

        result.IsValid.Should().BeFalse();
        result.Error!.Code.Should().Be(AppointmentValidationErrorCodes.SlotUnavailable);
    }

    [Fact]
    public void Resolve_WhenSlotStartsInAmbiguousFallBackHour_ReturnsUnavailable()
    {
        var branch = CreateBranch(
            timeZoneId: "GMT Standard Time",
            dayOfWeek: DayOfWeek.Sunday,
            startLocalTime: TimeSpan.Zero,
            endLocalTime: TimeSpan.FromHours(4));

        var result = _resolver.Resolve(
            branch,
            new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc),
            30);

        result.IsValid.Should().BeFalse();
        result.Error!.Code.Should().Be(AppointmentValidationErrorCodes.SlotUnavailable);
    }

    private static Branch CreateBranch(
        int minimumLeadMinutes = 0,
        int bookingHorizonDays = 30,
        bool settingsActive = true,
        string timeZoneId = "UTC",
        DayOfWeek dayOfWeek = DayOfWeek.Monday,
        TimeSpan? startLocalTime = null,
        TimeSpan? endLocalTime = null,
        int slotDurationMinutes = 15,
        int capacity = 4)
    {
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            NameAr = "الفرع الرئيسي",
            AddressAr = "العنوان"
        };

        branch.AvailabilitySettings = new BranchAvailabilitySettings
        {
            BranchId = branch.Id,
            TimeZoneId = timeZoneId,
            MinimumLeadMinutes = minimumLeadMinutes,
            BookingHorizonDays = bookingHorizonDays,
            IsActive = settingsActive
        };

        branch.RecurringSchedules.Add(new BranchRecurringSchedule
        {
            BranchId = branch.Id,
            DayOfWeek = dayOfWeek,
            StartLocalTime = startLocalTime ?? TimeSpan.FromHours(9),
            EndLocalTime = endLocalTime ?? TimeSpan.FromHours(18),
            SlotDurationMinutes = slotDurationMinutes,
            Capacity = capacity,
            IsActive = true
        });

        return branch;
    }

    private sealed class FakeClock : ISystemClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
    }
}
