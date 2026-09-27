using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Validators.Internal;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies advisory availability discovery scheduling and capacity rules.
/// </summary>
public sealed class AvailabilityDiscoveryServiceTests
{
    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-SCHEDULE-014")]
    [Trait("ScenarioId", "FAN-AVAILABILITY-CAPACITY-017")]
    public async Task DiscoverAsync_SelectsNearestFreeSlotAndSubtractsReservations()
    {
        var now = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var company = new Company { Id = Guid.NewGuid(), NameAr = "شركة" };
        var branch = CreateBranch(company, capacity: 2);
        context.AddRange(company, branch);
        context.AppointmentReservations.Add(new AppointmentReservation
        {
            BranchId = branch.Id,
            CustomerBookingReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            RequestHash = "hash",
            Currency = "ILS",
            RequestedSlotStartUtc = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc),
            RequestedSlotEndUtc = new DateTime(2026, 9, 28, 10, 30, 0, DateTimeKind.Utc),
            Status = BookingStatuses.Confirmed,
            StatusChangedAtUtc = now,
            CreatedAtUtc = now
        });
        await context.SaveChangesAsync();
        var service = CreateService(context, now);

        var response = await service.DiscoverAsync(
            new AvailabilityDiscoveryRequest
            {
                Date = new DateOnly(2026, 9, 28),
                PreferredLocalTime = new TimeOnly(10, 10),
                Candidates =
                [
                    new AvailabilityDiscoveryCompanyCandidate
                    {
                        CompanyId = company.Id,
                        BranchIds = [branch.Id]
                    }
                ]
            },
            default);

        var result = response.Results.Should().ContainSingle().Subject;
        result.SlotStartLocal.Should().Be(new DateTime(2026, 9, 28, 10, 0, 0));
        result.ConfiguredCapacity.Should().Be(2);
        result.RemainingCapacity.Should().Be(1);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-OVERRIDE-015")]
    public async Task DiscoverAsync_ClosedOverrideReplacesRecurringSchedule()
    {
        var now = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var company = new Company { Id = Guid.NewGuid(), NameAr = "شركة" };
        var branch = CreateBranch(company, capacity: 2);
        branch.AvailabilityOverrides.Add(new BranchAvailabilityOverride
        {
            BranchId = branch.Id,
            OverrideDate = new DateOnly(2026, 9, 28),
            IsClosed = true,
            IsActive = true
        });
        context.AddRange(company, branch);
        await context.SaveChangesAsync();
        var service = CreateService(context, now);

        var response = await service.DiscoverAsync(
            new AvailabilityDiscoveryRequest
            {
                Date = new DateOnly(2026, 9, 28),
                PreferredLocalTime = new TimeOnly(10, 0),
                Candidates =
                [
                    new AvailabilityDiscoveryCompanyCandidate
                    {
                        CompanyId = company.Id,
                        BranchIds = [branch.Id]
                    }
                ]
            },
            default);

        response.Results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("company-mismatch")]
    [InlineData("company-inactive")]
    [InlineData("branch-inactive")]
    [InlineData("settings-missing")]
    [InlineData("settings-inactive")]
    public async Task DiscoverAsync_RejectsBranchesOutsideActiveOwnedAvailability(
        string condition)
    {
        var now = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة",
            IsActive = condition != "company-inactive"
        };
        var branch = CreateBranch(company, capacity: 2);
        branch.IsActive = condition != "branch-inactive";
        if (condition == "settings-missing")
        {
            branch.AvailabilitySettings = null;
        }
        else if (condition == "settings-inactive")
        {
            branch.AvailabilitySettings!.IsActive = false;
        }

        context.AddRange(company, branch);
        await context.SaveChangesAsync();

        var response = await DiscoverAsync(
            context,
            now,
            condition == "company-mismatch" ? Guid.NewGuid() : company.Id,
            [branch.Id]);

        response.Results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid-time-zone")]
    [InlineData("past")]
    [InlineData("beyond-horizon")]
    [InlineData("before-lead")]
    public async Task DiscoverAsync_FailsClosedForInvalidTimeAndBookingBoundaries(
        string condition)
    {
        var now = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var company = new Company { Id = Guid.NewGuid(), NameAr = "شركة", IsActive = true };
        var branch = CreateBranch(company, capacity: 2);
        branch.AvailabilitySettings!.TimeZoneId =
            condition == "invalid-time-zone" ? "Not/A-Time-Zone" : "UTC";
        branch.AvailabilitySettings.BookingHorizonDays =
            condition == "beyond-horizon" ? 0 : 30;
        branch.AvailabilitySettings.MinimumLeadMinutes =
            condition == "before-lead" ? 24 * 60 * 2 : 0;
        context.AddRange(company, branch);
        await context.SaveChangesAsync();
        var date = condition == "past"
            ? new DateOnly(2026, 9, 21)
            : new DateOnly(2026, 9, 28);

        var response = await DiscoverAsync(
            context,
            now,
            company.Id,
            [branch.Id],
            date);

        response.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_OpenOverrideReplacesRecurringCapacityAndWindow()
    {
        var now = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var company = new Company { Id = Guid.NewGuid(), NameAr = "شركة", IsActive = true };
        var branch = CreateBranch(company, capacity: 2);
        branch.AvailabilityOverrides.Add(new BranchAvailabilityOverride
        {
            BranchId = branch.Id,
            OverrideDate = new DateOnly(2026, 9, 28),
            IsClosed = false,
            StartLocalTime = TimeSpan.FromHours(14),
            EndLocalTime = TimeSpan.FromHours(16),
            SlotDurationMinutes = 60,
            Capacity = 7,
            IsActive = true
        });
        context.AddRange(company, branch);
        await context.SaveChangesAsync();

        var response = await DiscoverAsync(
            context,
            now,
            company.Id,
            [branch.Id],
            preferredTime: new TimeOnly(10));

        var result = response.Results.Should().ContainSingle().Subject;
        result.SlotStartLocal.Should().Be(new DateTime(2026, 9, 28, 14, 0, 0));
        result.ConfiguredCapacity.Should().Be(7);
        result.RemainingCapacity.Should().Be(7);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-TIME-016")]
    public async Task DiscoverAsync_OvernightScheduleReturnsOnlyRequestedDateSlots()
    {
        var now = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var company = new Company { Id = Guid.NewGuid(), NameAr = "شركة", IsActive = true };
        var branch = CreateBranch(
            company,
            capacity: 3,
            dayOfWeek: DayOfWeek.Sunday,
            start: TimeSpan.FromHours(22),
            end: TimeSpan.FromHours(2));
        context.AddRange(company, branch);
        await context.SaveChangesAsync();

        var response = await DiscoverAsync(
            context,
            now,
            company.Id,
            [branch.Id],
            preferredTime: new TimeOnly(0, 30));

        var result = response.Results.Single();
        result.TimeZoneId.Should().Be("UTC");
        result.SlotStartLocal.Should().Be(
            new DateTime(2026, 9, 28, 0, 30, 0, DateTimeKind.Unspecified));
        result.SlotStartUtc.Should().Be(
            new DateTime(2026, 9, 28, 0, 30, 0, DateTimeKind.Utc));
        result.BranchId.Should().Be(branch.Id);
    }

    [Theory]
    [InlineData(2026, 3, 29, 1, 0)]
    [InlineData(2026, 10, 25, 1, 0)]
    public async Task DiscoverAsync_DstInvalidOrAmbiguousSlotsAreExcluded(
        int year,
        int month,
        int day,
        int hour,
        int minute)
    {
        var date = new DateOnly(year, month, day);
        var now = new DateTime(year, month, day - 1, 0, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var company = new Company { Id = Guid.NewGuid(), NameAr = "شركة", IsActive = true };
        var branch = CreateBranch(
            company,
            capacity: 2,
            dayOfWeek: DayOfWeek.Sunday,
            start: TimeSpan.FromHours(hour),
            end: TimeSpan.FromHours(hour + 1));
        branch.AvailabilitySettings!.TimeZoneId = "GMT Standard Time";
        context.AddRange(company, branch);
        await context.SaveChangesAsync();

        var response = await DiscoverAsync(
            context,
            now,
            company.Id,
            [branch.Id],
            date,
            new TimeOnly(hour, minute));

        response.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_SelectsNearestBranchAndPreservesCandidateOrderForExactTie()
    {
        var now = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var company = new Company { Id = Guid.NewGuid(), NameAr = "شركة", IsActive = true };
        var first = CreateBranch(company, 2);
        var second = CreateBranch(company, 2);
        first.RecurringSchedules.Single().StartLocalTime = TimeSpan.FromHours(9);
        first.RecurringSchedules.Single().EndLocalTime = TimeSpan.FromHours(10);
        second.RecurringSchedules.Single().StartLocalTime = TimeSpan.FromHours(10);
        second.RecurringSchedules.Single().EndLocalTime = TimeSpan.FromHours(11);
        context.AddRange(company, first, second);
        await context.SaveChangesAsync();

        var nearest = await DiscoverAsync(
            context,
            now,
            company.Id,
            [first.Id, second.Id],
            preferredTime: new TimeOnly(10, 10));
        nearest.Results.Single().BranchId.Should().Be(second.Id);

        second.RecurringSchedules.Single().StartLocalTime = TimeSpan.FromHours(9);
        second.RecurringSchedules.Single().EndLocalTime = TimeSpan.FromHours(10);
        await context.SaveChangesAsync();
        var tied = await DiscoverAsync(
            context,
            now,
            company.Id,
            [second.Id, first.Id],
            preferredTime: new TimeOnly(9));
        tied.Results.Single().BranchId.Should().Be(second.Id);
    }

    [Fact]
    public async Task DiscoverAsync_CountsEveryConsumingStatusButNotTerminalOrTouchingBoundaries()
    {
        var now = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var company = new Company { Id = Guid.NewGuid(), NameAr = "شركة", IsActive = true };
        var branch = CreateBranch(company, capacity: 5);
        context.AddRange(company, branch);
        var slotStart = new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);
        foreach (var status in BookingStatuses.CapacityOccupying)
        {
            context.AppointmentReservations.Add(CreateReservation(
                branch.Id,
                status,
                slotStart,
                slotStart.AddMinutes(30),
                now));
        }
        foreach (var status in BookingStatuses.Terminal)
        {
            context.AppointmentReservations.Add(CreateReservation(
                branch.Id,
                status,
                slotStart,
                slotStart.AddMinutes(30),
                now));
        }
        context.AppointmentReservations.AddRange(
            CreateReservation(
                branch.Id,
                BookingStatuses.Confirmed,
                slotStart.AddMinutes(-30),
                slotStart,
                now),
            CreateReservation(
                branch.Id,
                BookingStatuses.Confirmed,
                slotStart.AddMinutes(30),
                slotStart.AddMinutes(60),
                now));
        await context.SaveChangesAsync();

        var response = await DiscoverAsync(
            context,
            now,
            company.Id,
            [branch.Id],
            preferredTime: new TimeOnly(9));

        var result = response.Results.Single();
        result.ConfiguredCapacity.Should().Be(5);
        result.RemainingCapacity.Should().Be(
            5 - BookingStatuses.CapacityOccupying.Count);
        context.AppointmentReservations.Should().HaveCount(
            BookingStatuses.CapacityOccupying.Count + BookingStatuses.Terminal.Count + 2);
    }

    [Fact]
    public async Task DiscoverAsync_DoesNotRequireOfferingsOrCreateReservations()
    {
        var now = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
        await using var context = CreateContext();
        var company = new Company { Id = Guid.NewGuid(), NameAr = "شركة", IsActive = true };
        var branch = CreateBranch(company, capacity: 2);
        context.AddRange(company, branch);
        await context.SaveChangesAsync();

        var before = await context.AppointmentReservations.CountAsync();
        var response = await DiscoverAsync(context, now, company.Id, [branch.Id]);
        var after = await context.AppointmentReservations.CountAsync();

        response.Results.Should().ContainSingle();
        context.ServiceOfferings.Should().BeEmpty();
        after.Should().Be(before);
    }

    private static AvailabilityDiscoveryService CreateService(
        BusinessDbContext context,
        DateTime now) => new(
        context,
        new ServiceAreaCalculator(),
        new FakeClock(now),
        new AvailabilityDiscoveryRequestValidator());

    private static BusinessDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase($"availability-discovery-{Guid.NewGuid():N}")
            .Options;
        return new BusinessDbContext(options);
    }

    private static Task<AvailabilityDiscoveryResponse> DiscoverAsync(
        BusinessDbContext context,
        DateTime now,
        Guid companyId,
        IReadOnlyCollection<Guid> branchIds,
        DateOnly? date = null,
        TimeOnly? preferredTime = null) =>
        CreateService(context, now).DiscoverAsync(
            new AvailabilityDiscoveryRequest
            {
                Date = date ?? new DateOnly(2026, 9, 28),
                PreferredLocalTime = preferredTime ?? new TimeOnly(10),
                Candidates =
                [
                    new AvailabilityDiscoveryCompanyCandidate
                    {
                        CompanyId = companyId,
                        BranchIds = branchIds
                    }
                ]
            },
            default);

    private static AppointmentReservation CreateReservation(
        Guid branchId,
        string status,
        DateTime start,
        DateTime end,
        DateTime now) => new()
    {
        BranchId = branchId,
        CustomerBookingReference = Guid.NewGuid(),
        OrderGuid = Guid.NewGuid(),
        RequestHash = Guid.NewGuid().ToString("N"),
        Currency = "ILS",
        RequestedSlotStartUtc = start,
        RequestedSlotEndUtc = end,
        Status = status,
        StatusChangedAtUtc = now,
        CreatedAtUtc = now
    };

    private static Branch CreateBranch(
        Company company,
        int capacity,
        DayOfWeek dayOfWeek = DayOfWeek.Monday,
        TimeSpan? start = null,
        TimeSpan? end = null)
    {
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            Company = company,
            CompanyId = company.Id,
            NameAr = "فرع",
            AddressAr = "عنوان",
            IsActive = true
        };
        branch.AvailabilitySettings = new BranchAvailabilitySettings
        {
            BranchId = branch.Id,
            Branch = branch,
            TimeZoneId = "UTC",
            BookingHorizonDays = 30,
            MinimumLeadMinutes = 0,
            IsActive = true
        };
        branch.RecurringSchedules.Add(new BranchRecurringSchedule
        {
            BranchId = branch.Id,
            Branch = branch,
            DayOfWeek = dayOfWeek,
            StartLocalTime = start ?? TimeSpan.FromHours(9),
            EndLocalTime = end ?? TimeSpan.FromHours(12),
            SlotDurationMinutes = 30,
            Capacity = capacity,
            IsActive = true
        });
        return branch;
    }

    private sealed class FakeClock(DateTime utcNow) : ISystemClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
