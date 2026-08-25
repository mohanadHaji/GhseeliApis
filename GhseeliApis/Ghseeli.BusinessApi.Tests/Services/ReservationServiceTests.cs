using FluentAssertions;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.Bookings;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies authoritative reservation persistence and replay behavior.
/// </summary>
public class ReservationServiceTests
{
    [Fact]
    public async Task CreateAsync_WithMatchingProof_PersistsOneReservationAndReplays()
    {
        var fixture = await CreateFixtureAsync(capacity: 2);

        var first = await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);
        var second = await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);

        second.ReservationId.Should().Be(first.ReservationId);
        second.WorkOrderId.Should().Be(first.WorkOrderId);
        (await fixture.Context.AppointmentReservations.CountAsync()).Should().Be(1);
        (await fixture.Context.WorkOrders.CountAsync()).Should().Be(1);
        (await fixture.Context.WorkOrderItems.CountAsync()).Should().Be(1);
        var reservation = await fixture.Context.AppointmentReservations
            .Include(value => value.WorkOrder)
            .SingleAsync();
        reservation.BusinessVerticalId.Should().Be(BusinessVerticalDefaults.CarWashId);
        reservation.BusinessVerticalCode.Should().Be(BusinessVerticalDefaults.CarWashCode);
        reservation.WorkOrder.BusinessVerticalId.Should().Be(reservation.BusinessVerticalId);
        reservation.WorkOrder.BusinessVerticalCode.Should().Be(reservation.BusinessVerticalCode);
        reservation.WorkOrder.VehicleType.Should().Be(fixture.Request.Vehicle.VehicleType);
        fixture.ValidationService.Verify(
            service => service.ValidateAsync(It.IsAny<ValidateAppointmentRequest>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenAuthoritativePriceChanged_RejectsWithoutPersistence()
    {
        var fixture = await CreateFixtureAsync(capacity: 2);
        fixture.Request.Items.Single().ExpectedItemSubtotal++;

        var action = () => fixture.Service.CreateAsync(
            fixture.Request,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ReservationRejectedException>();
        exception.Which.Code.Should().Be(ReservationErrorCodes.PriceChanged);
        (await fixture.Context.AppointmentReservations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WhenCapacityIsConsumed_RejectsWithoutDuplicateWorkOrder()
    {
        var fixture = await CreateFixtureAsync(capacity: 1);
        await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);
        fixture.Request.OrderGuid = Guid.NewGuid();
        fixture.Request.BookingReference = Guid.NewGuid();

        var action = () => fixture.Service.CreateAsync(
            fixture.Request,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ReservationRejectedException>();
        exception.Which.Code.Should().Be(ReservationErrorCodes.SlotUnavailable);
        (await fixture.Context.AppointmentReservations.CountAsync()).Should().Be(1);
        (await fixture.Context.WorkOrders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_LegacyReservedRecord_StillConsumesCapacity()
    {
        var fixture = await CreateFixtureAsync(capacity: 1);
        await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);
        var existing = await fixture.Context.AppointmentReservations.SingleAsync();
        existing.Status = "Reserved";
        await fixture.Context.SaveChangesAsync();
        fixture.Request.OrderGuid = Guid.NewGuid();
        fixture.Request.BookingReference = Guid.NewGuid();

        var action = () => fixture.Service.CreateAsync(
            fixture.Request,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ReservationRejectedException>();
        exception.Which.Code.Should().Be(ReservationErrorCodes.SlotUnavailable);
    }

    [Theory]
    [InlineData(BookingStatuses.Pending)]
    [InlineData(BookingStatuses.Confirmed)]
    [InlineData(BookingStatuses.InProgress)]
    public async Task CreateAsync_ActiveReservationStatus_ConsumesCapacity(string status)
    {
        var fixture = await CreateFixtureAsync(capacity: 1);
        await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);
        var existing = await fixture.Context.AppointmentReservations.SingleAsync();
        existing.Status = status;
        existing.WorkOrder.Status = status;
        await fixture.Context.SaveChangesAsync();
        fixture.Request.OrderGuid = Guid.NewGuid();
        fixture.Request.BookingReference = Guid.NewGuid();

        var action = () => fixture.Service.CreateAsync(
            fixture.Request,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ReservationRejectedException>();
        exception.Which.Code.Should().Be(ReservationErrorCodes.SlotUnavailable);
    }

    [Theory]
    [InlineData(BookingStatuses.Completed)]
    [InlineData(BookingStatuses.Cancelled)]
    [InlineData(BookingStatuses.NoShow)]
    public async Task CreateAsync_TerminalReservationStatus_ReleasesCapacity(string status)
    {
        var fixture = await CreateFixtureAsync(capacity: 1);
        await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);
        var existing = await fixture.Context.AppointmentReservations.SingleAsync();
        existing.Status = status;
        existing.WorkOrder.Status = status;
        await fixture.Context.SaveChangesAsync();
        fixture.Request.OrderGuid = Guid.NewGuid();
        fixture.Request.BookingReference = Guid.NewGuid();

        var result = await fixture.Service.CreateAsync(
            fixture.Request,
            CancellationToken.None);

        result.Status.Should().Be(BookingStatuses.Pending);
        (await fixture.Context.AppointmentReservations.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_WhenOrderGuidIsReusedWithDifferentContent_Rejects()
    {
        var fixture = await CreateFixtureAsync(capacity: 2);
        await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);
        fixture.Request.BookingReference = Guid.NewGuid();

        var action = () => fixture.Service.CreateAsync(
            fixture.Request,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ReservationRejectedException>();
        exception.Which.Code.Should().Be(ReservationErrorCodes.Invalid);
        (await fixture.Context.AppointmentReservations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenItemsContainNull_RejectsBeforeValidationOrPersistence()
    {
        var fixture = await CreateFixtureAsync(capacity: 2);
        fixture.Request.Items = [null!];

        var action = () => fixture.Service.CreateAsync(
            fixture.Request,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ReservationRejectedException>();
        exception.Which.Code.Should().Be(ReservationErrorCodes.Invalid);
        fixture.ValidationService.Verify(
            service => service.ValidateAsync(It.IsAny<ValidateAppointmentRequest>()),
            Times.Never);
        (await fixture.Context.AppointmentReservations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WhenSelectedAddonsIsNull_RejectsBeforeValidationOrHashing()
    {
        var fixture = await CreateFixtureAsync(capacity: 2);
        fixture.Request.Items.Single().SelectedAddons = null!;

        var action = () => fixture.Service.CreateAsync(
            fixture.Request,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ReservationRejectedException>();
        exception.Which.Code.Should().Be(ReservationErrorCodes.Invalid);
        fixture.ValidationService.Verify(
            service => service.ValidateAsync(It.IsAny<ValidateAppointmentRequest>()),
            Times.Never);
        (await fixture.Context.AppointmentReservations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WhenSelectedAddonsContainNull_RejectsBeforeValidationOrHashing()
    {
        var fixture = await CreateFixtureAsync(capacity: 2);
        fixture.Request.Items.Single().SelectedAddons = [null!];

        var action = () => fixture.Service.CreateAsync(
            fixture.Request,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ReservationRejectedException>();
        exception.Which.Code.Should().Be(ReservationErrorCodes.Invalid);
        fixture.ValidationService.Verify(
            service => service.ValidateAsync(It.IsAny<ValidateAppointmentRequest>()),
            Times.Never);
        (await fixture.Context.AppointmentReservations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WhenInactiveClosureExists_UsesRecurringCapacity()
    {
        var fixture = await CreateFixtureAsync(capacity: 1);
        fixture.Context.BranchAvailabilityOverrides.Add(new BranchAvailabilityOverride
        {
            BranchId = fixture.Request.BranchId,
            OverrideDate = DateOnly.FromDateTime(fixture.Request.RequestedSlotStartUtc.UtcDateTime),
            IsClosed = true,
            IsActive = false
        });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);

        result.Status.Should().Be(ReservationStatuses.Pending);
        (await fixture.Context.AppointmentReservations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenInactiveCapacityOverrideExists_UsesRecurringCapacity()
    {
        var fixture = await CreateFixtureAsync(capacity: 1);
        fixture.Context.BranchAvailabilityOverrides.Add(new BranchAvailabilityOverride
        {
            BranchId = fixture.Request.BranchId,
            OverrideDate = DateOnly.FromDateTime(fixture.Request.RequestedSlotStartUtc.UtcDateTime),
            StartLocalTime = TimeSpan.FromHours(8),
            EndLocalTime = TimeSpan.FromHours(18),
            SlotDurationMinutes = 30,
            Capacity = 0,
            IsActive = false
        });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);

        result.Status.Should().Be(ReservationStatuses.Pending);
    }

    [Fact]
    public async Task CreateAsync_WithReorderedSelections_ReplaysSameReservation()
    {
        var fixture = await CreateFixtureAsync(capacity: 2);
        var firstChoice = Guid.NewGuid();
        var secondChoice = Guid.NewGuid();
        fixture.Request.Items.Single().SelectedAddons =
        [
            new() { AddonChoiceId = firstChoice, Quantity = 1 },
            new() { AddonChoiceId = secondChoice, Quantity = 2 }
        ];

        var first = await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);
        fixture.Request.Items.Single().SelectedAddons =
        [
            new() { AddonChoiceId = secondChoice, Quantity = 2 },
            new() { AddonChoiceId = firstChoice, Quantity = 1 }
        ];
        var replay = await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);

        replay.ReservationId.Should().Be(first.ReservationId);
        fixture.ValidationService.Verify(
            service => service.ValidateAsync(It.IsAny<ValidateAppointmentRequest>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenCatalogChangesDuringAuthoritativeValidation_RejectsWithoutPersistence()
    {
        var fixture = await CreateFixtureAsync(capacity: 2);
        fixture.ValidationService
            .Setup(service => service.ValidateAsync(It.IsAny<ValidateAppointmentRequest>()))
            .ReturnsAsync(() =>
            {
                var changed = fixture.Validation;
                changed.CatalogVersion = fixture.Request.ExpectedCatalogVersion + 1;
                return changed;
            });

        var action = () => fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ReservationRejectedException>();
        exception.Which.Code.Should().Be(ReservationErrorCodes.CatalogChanged);
        (await fixture.Context.AppointmentReservations.CountAsync()).Should().Be(0);
        (await fixture.Context.WorkOrders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WithMultipleItems_PersistsCanonicalItemAndSelectionOrderAtomically()
    {
        var fixture = await CreateFixtureAsync(capacity: 2);
        var lowerOfferingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var higherOfferingId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        fixture.Request.Items =
        [
            CreateItem(higherOfferingId, 40m, 10),
            CreateItem(lowerOfferingId, 70m, 35)
        ];
        fixture.Request.ExpectedItemSubtotal = 110m;
        fixture.Request.ExpectedTotalDurationMinutes = 45;
        fixture.ValidationService
            .Setup(service => service.ValidateAsync(It.IsAny<ValidateAppointmentRequest>()))
            .ReturnsAsync((ValidateAppointmentRequest value) => new ValidateAppointmentResponse
            {
                Valid = true,
                CatalogVersion = 5,
                Currency = "ILS",
                BranchId = value.BranchId,
                OfferingId = value.OfferingId,
                BaseSubtotal = value.OfferingId == lowerOfferingId ? 70m : 40m,
                TotalPrice = value.OfferingId == lowerOfferingId ? 70m : 40m,
                TotalDurationMinutes = value.OfferingId == lowerOfferingId ? 35 : 10,
                NormalizedSelections = value.SelectedAddons
                    .Select((selection, index) => new NormalizedAddonSelection
                    {
                        AddonGroupId = Guid.Empty,
                        AddonChoiceId = selection.AddonChoiceId,
                        SelectionType = "Multiple",
                        Quantity = selection.Quantity
                    }).Reverse().ToArray()
            });

        var response = await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);

        response.Items.Select(item => item.OfferingId)
            .Should().Equal(lowerOfferingId, higherOfferingId);
        response.Items.Should().OnlyContain(item =>
            item.Selections.Select(selection => selection.AddonChoiceId)
                .SequenceEqual(item.Selections.Select(selection => selection.AddonChoiceId).Order()));
        (await fixture.Context.WorkOrders.CountAsync()).Should().Be(1);
        (await fixture.Context.WorkOrderItems.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_WhenSecondItemIsRejected_PersistsNothing()
    {
        var fixture = await CreateFixtureAsync(capacity: 2);
        var rejectedOfferingId = Guid.NewGuid();
        var acceptedItem = fixture.Request.Items.Single();
        fixture.Request.Items =
        [
            acceptedItem,
            CreateItem(rejectedOfferingId, 25m, 30)
        ];
        fixture.Request.ExpectedItemSubtotal = 135m;
        fixture.Request.ExpectedTotalDurationMinutes = 75;
        fixture.ValidationService
            .Setup(service => service.ValidateAsync(
                It.Is<ValidateAppointmentRequest>(value => value.OfferingId == rejectedOfferingId)))
            .ReturnsAsync(new ValidateAppointmentResponse
            {
                Valid = false,
                CatalogVersion = 5,
                Errors =
                [
                    new AppointmentValidationIssue
                    {
                        Code = AppointmentValidationErrorCodes.OfferingInactive,
                        Message = "Inactive"
                    }
                ]
            });

        var action = () => fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);

        await action.Should().ThrowAsync<ReservationRejectedException>();
        (await fixture.Context.AppointmentReservations.CountAsync()).Should().Be(0);
        (await fixture.Context.WorkOrders.CountAsync()).Should().Be(0);
        (await fixture.Context.WorkOrderItems.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_CreatesPendingReservationWithoutExpiry()
    {
        var fixture = await CreateFixtureAsync(capacity: 2);

        var response = await fixture.Service.CreateAsync(fixture.Request, CancellationToken.None);

        response.Status.Should().Be(ReservationStatuses.Pending);
        response.ReservationExpiresAtUtc.Should().BeNull();
        (await fixture.Context.AppointmentReservations.SingleAsync()).ExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WhenCapacityOneIsRequestedConcurrently_OnlyOneAtomicWorkOrderIsCreated()
    {
        var databaseName = $"GhseeliReservation_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        var branchId = Guid.NewGuid();
        var offeringId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

        try
        {
            await using (var setup = new BusinessDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var companyId = Guid.NewGuid();
                setup.Companies.Add(new Company
                {
                    Id = companyId,
                    NameAr = "Company",
                    CatalogVersion = 5
                });
                setup.Branches.Add(new Branch
                {
                    Id = branchId,
                    CompanyId = companyId,
                    NameAr = "Branch",
                    AddressAr = "Address"
                });
                setup.BranchAvailabilitySettings.Add(new BranchAvailabilitySettings
                {
                    BranchId = branchId,
                    TimeZoneId = "UTC",
                    BookingHorizonDays = 30,
                    IsActive = true
                });
                setup.BranchRecurringSchedules.Add(new BranchRecurringSchedule
                {
                    BranchId = branchId,
                    DayOfWeek = start.DayOfWeek,
                    StartLocalTime = TimeSpan.FromHours(8),
                    EndLocalTime = TimeSpan.FromHours(18),
                    SlotDurationMinutes = 15,
                    Capacity = 1,
                    IsActive = true
                });
                await setup.SaveChangesAsync();
            }

            var release = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var requests = new[]
            {
                CreateRequest(branchId, offeringId, start),
                CreateRequest(branchId, offeringId, start)
            };
            var attempts = requests.Select(async request =>
            {
                await using var context = new BusinessDbContext(options);
                var validation = new Mock<IAppointmentValidationService>();
                validation.Setup(service => service.ValidateAsync(
                        It.IsAny<ValidateAppointmentRequest>()))
                    .ReturnsAsync((ValidateAppointmentRequest value) =>
                        CreateValidation(value.BranchId, value.OfferingId));
                var clock = Mock.Of<ISystemClock>(
                    value => value.UtcNow ==
                        new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc));
                var service = new ReservationService(
                    context,
                    validation.Object,
                    new TimeZoneAvailabilityResolver(clock),
                    clock,
                    Mock.Of<IAppLogger>());
                try
                {
                    await release.Task;
                    await service.CreateAsync(request, CancellationToken.None);
                    return true;
                }
                catch (ReservationRejectedException exception)
                    when (exception.Code == ReservationErrorCodes.SlotUnavailable)
                {
                    return false;
                }
            });

            release.TrySetResult();
            (await Task.WhenAll(attempts)).Should().ContainSingle(value => value);
            await using var verification = new BusinessDbContext(options);
            (await verification.AppointmentReservations.CountAsync()).Should().Be(1);
            (await verification.WorkOrders.CountAsync()).Should().Be(1);
            (await verification.WorkOrderItems.CountAsync()).Should().Be(1);
        }
        finally
        {
            await using var cleanup = new BusinessDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static ValidateAppointmentResponse CreateValidation(Guid branchId, Guid offeringId) =>
        new()
        {
            Valid = true,
            CatalogVersion = 5,
            Currency = "ILS",
            BranchId = branchId,
            OfferingId = offeringId,
            BaseSubtotal = 100m,
            AddonSubtotal = 10m,
            TotalPrice = 110m,
            TotalDurationMinutes = 45
        };

    private static CreateReservationRequest CreateRequest(
        Guid branchId,
        Guid offeringId,
        DateTimeOffset start) =>
        new()
        {
            BookingReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            BranchId = branchId,
            ExpectedCatalogVersion = 5,
            RequestedSlotStartUtc = start,
            Currency = "ILS",
            ExpectedItemSubtotal = 110m,
            ExpectedTotalDurationMinutes = 45,
            Customer = new ReservationCustomerSnapshot { Name = "Customer" },
            Vehicle = new ReservationVehicleSnapshot { VehicleType = "Sedan" },
            Location = new ReservationLocationSnapshot
            {
                AddressLine = "Street 1",
                Latitude = 32.1,
                Longitude = 34.8
            },
            CancellationPolicyAcknowledged = true,
            Items =
            [
                new CreateReservationItemRequest
                {
                    OfferingId = offeringId,
                    ExpectedBaseSubtotal = 100m,
                    ExpectedAddonSubtotal = 10m,
                    ExpectedItemSubtotal = 110m,
                    ExpectedDurationMinutes = 45
                }
            ]
        };

    private static CreateReservationItemRequest CreateItem(
        Guid offeringId,
        decimal subtotal,
        int duration) =>
        new()
        {
            OfferingId = offeringId,
            ExpectedBaseSubtotal = subtotal,
            ExpectedItemSubtotal = subtotal,
            ExpectedDurationMinutes = duration,
            SelectedAddons =
            [
                new() { AddonChoiceId = Guid.NewGuid(), Quantity = 1 },
                new() { AddonChoiceId = Guid.NewGuid(), Quantity = 2 }
            ]
        };

    private static async Task<Fixture> CreateFixtureAsync(int capacity)
    {
        var start = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
        var branchId = Guid.NewGuid();
        var offeringId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var context = new BusinessDbContext(options);
        var companyId = Guid.NewGuid();
        context.Companies.Add(new Company
        {
            Id = companyId,
            NameAr = "Company",
            CatalogVersion = 5
        });
        context.Branches.Add(new Branch
        {
            Id = branchId,
            CompanyId = companyId,
            NameAr = "Branch",
            AddressAr = "Address"
        });
        context.BranchAvailabilitySettings.Add(new BranchAvailabilitySettings
        {
            BranchId = branchId,
            TimeZoneId = "UTC",
            BookingHorizonDays = 30,
            IsActive = true
        });
        context.BranchRecurringSchedules.Add(new BranchRecurringSchedule
        {
            BranchId = branchId,
            DayOfWeek = start.DayOfWeek,
            StartLocalTime = TimeSpan.FromHours(8),
            EndLocalTime = TimeSpan.FromHours(18),
            SlotDurationMinutes = 15,
            Capacity = capacity,
            IsActive = true
        });
        await context.SaveChangesAsync();

        var validation = new ValidateAppointmentResponse
        {
            Valid = true,
            CatalogVersion = 5,
            Currency = "ILS",
            BranchId = branchId,
            OfferingId = offeringId,
            BaseSubtotal = 100m,
            AddonSubtotal = 10m,
            TotalPrice = 110m,
            TotalDurationMinutes = 45,
            NormalizedSelections =
            [
                new NormalizedAddonSelection
                {
                    AddonGroupId = Guid.NewGuid(),
                    AddonChoiceId = Guid.NewGuid(),
                    SelectionType = "Single",
                    Quantity = 1,
                    UnitPriceAdjustment = 10m,
                    TotalPriceAdjustment = 10m,
                    UnitDurationAdjustmentMinutes = 15,
                    TotalDurationAdjustmentMinutes = 15
                }
            ]
        };
        var validationService = new Mock<IAppointmentValidationService>();
        validationService.Setup(service => service.ValidateAsync(
                It.IsAny<ValidateAppointmentRequest>()))
            .ReturnsAsync(validation);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(
            new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc));

        var request = new CreateReservationRequest
        {
            BookingReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            BranchId = branchId,
            ExpectedCatalogVersion = 5,
            RequestedSlotStartUtc = start,
            Currency = "ILS",
            ExpectedItemSubtotal = 110m,
            ExpectedTotalDurationMinutes = 45,
            Customer = new ReservationCustomerSnapshot { Name = "Customer" },
            Vehicle = new ReservationVehicleSnapshot { VehicleType = "Sedan" },
            Location = new ReservationLocationSnapshot
            {
                AddressLine = "Street 1",
                Latitude = 32.1,
                Longitude = 34.8
            },
            CancellationPolicyAcknowledged = true,
            Items =
            [
                new CreateReservationItemRequest
                {
                    OfferingId = offeringId,
                    ExpectedBaseSubtotal = 100m,
                    ExpectedAddonSubtotal = 10m,
                    ExpectedItemSubtotal = 110m,
                    ExpectedDurationMinutes = 45
                }
            ]
        };

        return new Fixture(
            context,
            new ReservationService(
                context,
                validationService.Object,
                new TimeZoneAvailabilityResolver(clock.Object),
                clock.Object,
                Mock.Of<IAppLogger>()),
            validationService,
            validation,
            request);
    }

    private sealed record Fixture(
        BusinessDbContext Context,
        ReservationService Service,
        Mock<IAppointmentValidationService> ValidationService,
        ValidateAppointmentResponse Validation,
        CreateReservationRequest Request);
}
