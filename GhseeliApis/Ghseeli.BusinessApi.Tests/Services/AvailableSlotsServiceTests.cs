using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.BusinessApi.Validators.Internal;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Defines authoritative slot generation and remaining-capacity behavior.
/// </summary>
public sealed class AvailableSlotsServiceTests
{
    [Fact]
    public async Task GetAsync_GeneratesAlignedSlotsAndSubtractsOverlappingCapacity()
    {
        await using var fixture = await CreateFixtureAsync(capacity: 2);
        fixture.Context.AppointmentReservations.Add(new AppointmentReservation
        {
            BranchId = fixture.Branch.Id,
            CustomerBookingReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            RequestHash = "hash",
            Currency = "ILS",
            RequestedSlotStartUtc = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc),
            RequestedSlotEndUtc = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc),
            Status = BookingStatuses.Pending,
            StatusChangedAtUtc = fixture.Clock.UtcNow,
            CreatedAtUtc = fixture.Clock.UtcNow
        });
        await fixture.Context.SaveChangesAsync();

        var response = await fixture.Service.GetAsync(CreateRequest(fixture), default);

        response.Valid.Should().BeTrue();
        response.TotalDurationMinutes.Should().Be(60);
        response.Slots.Should().HaveCount(5);
        response.Slots[0].StartLocal.Should().Be(
            new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Unspecified));
        response.Slots[0].ConfiguredCapacity.Should().Be(2);
        response.Slots[0].RemainingCapacity.Should().Be(1);
        response.Slots[1].RemainingCapacity.Should().Be(1);
        response.Slots[2].RemainingCapacity.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_WhenCapacityIsFull_OmitsSlotUnlessRequested()
    {
        await using var fixture = await CreateFixtureAsync(capacity: 1);
        fixture.Context.AppointmentReservations.Add(new AppointmentReservation
        {
            BranchId = fixture.Branch.Id,
            CustomerBookingReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            RequestHash = "hash",
            Currency = "ILS",
            RequestedSlotStartUtc = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc),
            RequestedSlotEndUtc = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc),
            Status = BookingStatuses.Confirmed,
            StatusChangedAtUtc = fixture.Clock.UtcNow,
            CreatedAtUtc = fixture.Clock.UtcNow
        });
        await fixture.Context.SaveChangesAsync();

        var hidden = await fixture.Service.GetAsync(CreateRequest(fixture), default);
        var request = CreateRequest(fixture);
        request.IncludeUnavailable = true;
        var included = await fixture.Service.GetAsync(request, default);

        hidden.Slots.Should().HaveCount(3);
        included.Slots.Should().HaveCount(5);
        included.Slots.Take(2).Should().OnlyContain(slot =>
            !slot.IsAvailable && slot.RemainingCapacity == 0);
    }

    [Fact]
    public async Task GetAsync_WhenDateIsClosed_ReturnsValidEmptyResult()
    {
        await using var fixture = await CreateFixtureAsync(capacity: 2);
        fixture.Branch.AvailabilityOverrides.Add(new BranchAvailabilityOverride
        {
            BranchId = fixture.Branch.Id,
            OverrideDate = new DateOnly(2026, 9, 7),
            IsClosed = true,
            IsActive = true
        });

        var response = await fixture.Service.GetAsync(CreateRequest(fixture), default);

        response.Valid.Should().BeTrue();
        response.Slots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_WhenCompanyDoesNotOwnBranch_ReturnsScopeError()
    {
        await using var fixture = await CreateFixtureAsync(capacity: 2);
        var request = CreateRequest(fixture);
        request.CompanyId = Guid.NewGuid();

        var response = await fixture.Service.GetAsync(request, default);

        response.Valid.Should().BeFalse();
        response.Errors.Should().ContainSingle(error =>
            error.Code == AppointmentValidationErrorCodes.BranchNotFound);
        response.Slots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_TerminalAndAdjacentReservations_DoNotConsumeCapacity()
    {
        await using var fixture = await CreateFixtureAsync(capacity: 1);
        fixture.Context.AppointmentReservations.AddRange(
            CreateReservation(
                fixture,
                BookingStatuses.Cancelled,
                new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc)),
            CreateReservation(
                fixture,
                BookingStatuses.Completed,
                new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc)),
            CreateReservation(
                fixture,
                BookingStatuses.Confirmed,
                new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc)));
        await fixture.Context.SaveChangesAsync();

        var response = await fixture.Service.GetAsync(CreateRequest(fixture), default);

        response.Slots.Should().HaveCount(5);
        response.Slots.Should().OnlyContain(slot =>
            slot.IsAvailable && slot.RemainingCapacity == 1);
    }

    [Theory]
    [InlineData(BookingStatuses.Pending, true)]
    [InlineData(BookingStatuses.Confirmed, true)]
    [InlineData(BookingStatuses.InProgress, true)]
    [InlineData(BookingStatuses.Completed, false)]
    [InlineData(BookingStatuses.Cancelled, false)]
    [InlineData(BookingStatuses.NoShow, false)]
    public async Task GetAsync_ReservationStatusControlsCapacity(
        string status,
        bool occupiesCapacity)
    {
        await using var fixture = await CreateFixtureAsync(capacity: 1);
        fixture.Context.AppointmentReservations.Add(CreateReservation(
            fixture,
            status,
            new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc)));
        await fixture.Context.SaveChangesAsync();
        var request = CreateRequest(fixture);
        request.IncludeUnavailable = true;

        var response = await fixture.Service.GetAsync(request, default);

        response.Slots.First().RemainingCapacity.Should()
            .Be(occupiesCapacity ? 0 : 1);
    }

    [Fact]
    public async Task GetAsync_MultipleOfferings_UsesCombinedAuthoritativeDuration()
    {
        await using var fixture = await CreateFixtureAsync(capacity: 2);
        var secondOfferingId = Guid.NewGuid();
        fixture.Validation.Setup(service => service.ValidateAsync(
                It.Is<ValidateAppointmentRequest>(request =>
                    request.BranchId == fixture.Branch.Id &&
                    request.OfferingId == secondOfferingId)))
            .ReturnsAsync((ValidateAppointmentRequest request) =>
                ValidValidation(fixture, secondOfferingId, request, 30));
        var request = CreateRequest(fixture);
        request.Items =
        [
            new AvailableSlotsItemRequest { OfferingId = fixture.OfferingId },
            new AvailableSlotsItemRequest { OfferingId = secondOfferingId }
        ];

        var response = await fixture.Service.GetAsync(request, default);

        response.Valid.Should().BeTrue();
        response.TotalDurationMinutes.Should().Be(90);
        response.Slots.Should().HaveCount(4);
        response.Slots.Last().StartLocal.Should().Be(
            new DateTime(2026, 9, 7, 10, 30, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public async Task GetAsync_ActiveOverride_ReplacesRecurringWindowAndCapacity()
    {
        await using var fixture = await CreateFixtureAsync(capacity: 2);
        fixture.Branch.AvailabilityOverrides.Add(new BranchAvailabilityOverride
        {
            BranchId = fixture.Branch.Id,
            OverrideDate = new DateOnly(2026, 9, 7),
            StartLocalTime = TimeSpan.FromHours(14),
            EndLocalTime = TimeSpan.FromHours(16),
            SlotDurationMinutes = 30,
            Capacity = 3,
            IsActive = true
        });

        var response = await fixture.Service.GetAsync(CreateRequest(fixture), default);

        response.Slots.Should().HaveCount(3);
        response.Slots.First().StartLocal.Hour.Should().Be(14);
        response.Slots.Should().OnlyContain(slot => slot.ConfiguredCapacity == 3);
    }

    [Fact]
    public async Task GetAsync_WhenAuthoritativeDurationIsZero_FailsClosed()
    {
        await using var fixture = await CreateFixtureAsync(capacity: 2);
        fixture.Validation.Setup(service => service.ValidateAsync(
                It.IsAny<ValidateAppointmentRequest>()))
            .ReturnsAsync((ValidateAppointmentRequest request) =>
                ValidValidation(fixture, request.OfferingId, request, 0));

        var response = await fixture.Service.GetAsync(CreateRequest(fixture), default);

        response.Valid.Should().BeFalse();
        response.Slots.Should().BeEmpty();
        response.Errors.Should().ContainSingle(error =>
            error.Code == AppointmentValidationErrorCodes.AddonSelectionRuleViolation);
    }

    [Fact]
    public async Task GetAsync_OverlappingScheduleDefinitions_DoNotDuplicateStarts()
    {
        await using var fixture = await CreateFixtureAsync(capacity: 2);
        fixture.Branch.RecurringSchedules.Add(new BranchRecurringSchedule
        {
            BranchId = fixture.Branch.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartLocalTime = TimeSpan.FromHours(9),
            EndLocalTime = TimeSpan.FromHours(12),
            SlotDurationMinutes = 30,
            Capacity = 2,
            IsActive = true
        });

        var response = await fixture.Service.GetAsync(CreateRequest(fixture), default);

        response.Slots.Should().HaveCount(5);
        response.Slots.Select(slot => slot.StartUtc).Should().OnlyHaveUniqueItems();
    }

    private static AvailableSlotsRequest CreateRequest(Fixture fixture) => new()
    {
        CompanyId = fixture.Branch.CompanyId,
        BranchId = fixture.Branch.Id,
        Date = new DateOnly(2026, 9, 7),
        Currency = "ILS",
        Items =
        [
            new AvailableSlotsItemRequest
            {
                OfferingId = fixture.OfferingId
            }
        ]
    };

    private static AppointmentReservation CreateReservation(
        Fixture fixture,
        string status,
        DateTime startUtc,
        DateTime endUtc) => new()
    {
        BranchId = fixture.Branch.Id,
        CustomerBookingReference = Guid.NewGuid(),
        OrderGuid = Guid.NewGuid(),
        RequestHash = Guid.NewGuid().ToString("N"),
        Currency = "ILS",
        RequestedSlotStartUtc = startUtc,
        RequestedSlotEndUtc = endUtc,
        Status = status,
        StatusChangedAtUtc = fixture.Clock.UtcNow,
        CreatedAtUtc = fixture.Clock.UtcNow
    };

    private static ValidateAppointmentResponse ValidValidation(
        Fixture fixture,
        Guid offeringId,
        ValidateAppointmentRequest request,
        int durationMinutes) => new()
    {
        Valid = true,
        CatalogVersion = fixture.Branch.Company.CatalogVersion,
        Currency = "ILS",
        BranchId = fixture.Branch.Id,
        OfferingId = offeringId,
        TotalDurationMinutes = durationMinutes,
        Availability = new AppointmentAvailabilityFacts
        {
            RequestedSlotStartUtc = request.RequestedSlotStartUtc.UtcDateTime,
            RequestedSlotEndUtc = request.RequestedSlotStartUtc.UtcDateTime
                .AddMinutes(durationMinutes)
        }
    };

    private static async Task<Fixture> CreateFixtureAsync(int capacity)
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var context = new BusinessDbContext(options);
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "Company",
            CatalogVersion = 9
        };
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "Branch",
            AddressAr = "Address",
            AvailabilitySettings = new BranchAvailabilitySettings
            {
                TimeZoneId = "UTC",
                BookingHorizonDays = 30,
                IsActive = true
            }
        };
        branch.AvailabilitySettings.BranchId = branch.Id;
        branch.RecurringSchedules.Add(new BranchRecurringSchedule
        {
            BranchId = branch.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartLocalTime = TimeSpan.FromHours(9),
            EndLocalTime = TimeSpan.FromHours(12),
            SlotDurationMinutes = 30,
            Capacity = capacity,
            IsActive = true
        });
        context.Companies.Add(company);
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var offeringId = Guid.NewGuid();
        var validation = new Mock<IAppointmentValidationService>();
        validation.Setup(service => service.ValidateAsync(
                It.Is<ValidateAppointmentRequest>(request =>
                    request.BranchId == branch.Id &&
                    request.OfferingId == offeringId)))
            .ReturnsAsync((ValidateAppointmentRequest request) => new ValidateAppointmentResponse
            {
                Valid = true,
                CatalogVersion = company.CatalogVersion,
                Currency = "ILS",
                BranchId = branch.Id,
                OfferingId = offeringId,
                TotalDurationMinutes = 60,
                Availability = new AppointmentAvailabilityFacts
                {
                    RequestedSlotStartUtc = request.RequestedSlotStartUtc.UtcDateTime,
                    RequestedSlotEndUtc = request.RequestedSlotStartUtc.UtcDateTime.AddHours(1)
                }
            });
        var repository = new Mock<IAvailabilityRepository>();
        repository.Setup(value => value.GetBranchWithAvailabilityAsync(branch.Id))
            .ReturnsAsync(branch);
        var clock = new FakeClock(
            new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));

        return new Fixture(
            context,
            branch,
            offeringId,
            clock,
            validation,
            new AvailableSlotsService(
                context,
                repository.Object,
                validation.Object,
                new TimeZoneAvailabilityResolver(clock),
                clock,
                new AvailableSlotsRequestValidator()));
    }

    private sealed record Fixture(
        BusinessDbContext Context,
        Branch Branch,
        Guid OfferingId,
        FakeClock Clock,
        Mock<IAppointmentValidationService> Validation,
        AvailableSlotsService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class FakeClock : ISystemClock
    {
        public FakeClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }
}
