using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Availability;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies cross-field availability management rules beyond request shape validation.
/// </summary>
public class AvailabilityRuleValidatorTests
{
    private readonly AvailabilityRuleValidator _validator = new();

    [Fact]
    public void ValidateSettings_RejectsUnknownTimeZone()
    {
        var settings = new BranchAvailabilitySettings
        {
            BranchId = Guid.NewGuid(),
            TimeZoneId = "Mars/Phobos",
            MinimumLeadMinutes = 0,
            BookingHorizonDays = 30,
            IsActive = true
        };

        var action = () => _validator.ValidateSettings(settings);

        action.Should().Throw<AvailabilityValidationException>()
            .WithMessage("*Time zone id is invalid*");
    }

    [Fact]
    public void ValidateRecurringSchedule_RejectsOverlappingActiveWindows()
    {
        var schedule = new BranchRecurringSchedule
        {
            Id = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            DayOfWeek = DayOfWeek.Monday,
            StartLocalTime = TimeSpan.FromHours(10),
            EndLocalTime = TimeSpan.FromHours(12),
            SlotDurationMinutes = 30,
            Capacity = 2,
            IsActive = true
        };
        var existing = new[]
        {
            new BranchRecurringSchedule
            {
                Id = Guid.NewGuid(),
                BranchId = schedule.BranchId,
                DayOfWeek = DayOfWeek.Monday,
                StartLocalTime = TimeSpan.FromHours(11),
                EndLocalTime = TimeSpan.FromHours(13),
                SlotDurationMinutes = 30,
                Capacity = 2,
                IsActive = true
            }
        };

        var action = () => _validator.ValidateRecurringSchedule(schedule, existing);

        action.Should().Throw<AvailabilityValidationException>()
            .WithMessage("*cannot overlap*");
    }

    [Fact]
    public void ValidateRecurringSchedule_AllowsOvernightWindowWhenItAlignsToSlots()
    {
        var schedule = new BranchRecurringSchedule
        {
            Id = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            DayOfWeek = DayOfWeek.Monday,
            StartLocalTime = TimeSpan.FromHours(22),
            EndLocalTime = TimeSpan.FromHours(2),
            SlotDurationMinutes = 30,
            Capacity = 2,
            IsActive = true
        };

        var action = () => _validator.ValidateRecurringSchedule(schedule, Array.Empty<BranchRecurringSchedule>());

        action.Should().NotThrow();
    }

    [Fact]
    public void ValidateAvailabilityOverride_RejectsClosedOverrideWithReplacementWindow()
    {
        var availabilityOverride = new BranchAvailabilityOverride
        {
            BranchId = Guid.NewGuid(),
            OverrideDate = new DateOnly(2026, 8, 24),
            IsClosed = true,
            StartLocalTime = TimeSpan.FromHours(9),
            IsActive = true
        };

        var action = () => _validator.ValidateAvailabilityOverride(
            availabilityOverride,
            Array.Empty<BranchAvailabilityOverride>());

        action.Should().Throw<AvailabilityValidationException>()
            .WithMessage("*Closed date overrides cannot define replacement hours*");
    }

    [Fact]
    public void ValidateAvailabilityOverride_AllowsOvernightReplacementWindow()
    {
        var availabilityOverride = new BranchAvailabilityOverride
        {
            BranchId = Guid.NewGuid(),
            OverrideDate = new DateOnly(2026, 8, 24),
            IsClosed = false,
            StartLocalTime = TimeSpan.FromHours(22),
            EndLocalTime = TimeSpan.FromHours(2),
            SlotDurationMinutes = 30,
            Capacity = 2,
            IsActive = true
        };

        var action = () => _validator.ValidateAvailabilityOverride(
            availabilityOverride,
            Array.Empty<BranchAvailabilityOverride>());

        action.Should().NotThrow();
    }

    [Fact]
    public void ValidateServiceArea_RejectsMissingBranchCoordinatesWhenCenterIsImplicit()
    {
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            NameAr = "الفرع الرئيسي",
            AddressAr = "العنوان"
        };
        var serviceArea = new BranchServiceArea
        {
            BranchId = branch.Id,
            RadiusKm = 10d,
            IsActive = true
        };

        var action = () => _validator.ValidateServiceArea(branch, serviceArea);

        action.Should().Throw<AvailabilityValidationException>()
            .WithMessage("*Branch coordinates are required*");
    }

    [Fact]
    public void ValidateServiceArea_RejectsRadiusAboveOperationalLimit()
    {
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            NameAr = "الفرع الرئيسي",
            AddressAr = "العنوان",
            Latitude = 24.7136,
            Longitude = 46.6753
        };
        var serviceArea = new BranchServiceArea
        {
            BranchId = branch.Id,
            RadiusKm = 500.01d,
            IsActive = true
        };

        var action = () => _validator.ValidateServiceArea(branch, serviceArea);

        action.Should().Throw<AvailabilityValidationException>()
            .WithMessage("*radius*");
    }
}
