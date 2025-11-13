using FluentAssertions;
using GhseeliApis.Handlers;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Repositories.Interfaces;
using Moq;

namespace GhseeliApis.Tests.Handlers;

/// <summary>
/// Unit tests for BookingHandler using Moq
/// </summary>
public class BookingHandlerTests
{
    private readonly Mock<IBookingRepository> _mockBookingRepository;
    private readonly Mock<IServiceOptionRepository> _mockServiceOptionRepository;
    private readonly Mock<IVehicleRepository> _mockVehicleRepository;
    private readonly Mock<IUserAddressRepository> _mockAddressRepository;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly BookingHandler _handler;
    private readonly Guid _testUserId;
    private readonly Guid _testCompanyId;

    public BookingHandlerTests()
    {
        _mockBookingRepository = new Mock<IBookingRepository>();
        _mockServiceOptionRepository = new Mock<IServiceOptionRepository>();
        _mockVehicleRepository = new Mock<IVehicleRepository>();
        _mockAddressRepository = new Mock<IUserAddressRepository>();
        _mockLogger = new Mock<IAppLogger>();
        
        _handler = new BookingHandler(
            _mockBookingRepository.Object,
            _mockServiceOptionRepository.Object,
            _mockVehicleRepository.Object,
            _mockAddressRepository.Object,
            _mockLogger.Object);
        
        _testUserId = Guid.NewGuid();
        _testCompanyId = Guid.NewGuid();
    }

    #region GetAllAsync Tests

    [Fact]
    public async Task GetAllAsync_ReturnsAllBookings()
    {
        // Arrange
        var bookings = new List<Booking>
        {
            new Booking { Id = Guid.NewGuid(), UserId = _testUserId },
            new Booking { Id = Guid.NewGuid(), UserId = Guid.NewGuid() },
            new Booking { Id = Guid.NewGuid(), UserId = _testUserId }
        };
        _mockBookingRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(bookings);

        // Act
        var result = await _handler.GetAllAsync();

        // Assert
        result.Should().HaveCount(3);
        _mockBookingRepository.Verify(r => r.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoBookings()
    {
        // Arrange
        _mockBookingRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<Booking>());

        // Act
        var result = await _handler.GetAllAsync();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_ReturnsBooking_WhenExists()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var booking = new Booking { Id = bookingId, UserId = _testUserId };
        _mockBookingRepository.Setup(r => r.GetByIdWithDetailsAsync(bookingId))  // Changed from GetByIdAsync
            .ReturnsAsync(booking);

        // Act
        var result = await _handler.GetByIdAsync(bookingId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(bookingId);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        _mockBookingRepository.Setup(r => r.GetByIdWithDetailsAsync(bookingId))  // Changed from GetByIdAsync
            .ReturnsAsync((Booking?)null);

        // Act
        var result = await _handler.GetByIdAsync(bookingId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetByCompanyIdAsync Tests

    [Fact]
    public async Task GetByCompanyIdAsync_ReturnsCompanyBookings()
    {
        // Arrange
        var bookings = new List<Booking>
        {
            new Booking { Id = Guid.NewGuid(), CompanyId = _testCompanyId },
            new Booking { Id = Guid.NewGuid(), CompanyId = _testCompanyId }
        };
        _mockBookingRepository.Setup(r => r.GetByCompanyIdAsync(_testCompanyId))
            .ReturnsAsync(bookings);

        // Act
        var result = await _handler.GetByCompanyIdAsync(_testCompanyId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(b => b.CompanyId.Should().Be(_testCompanyId));
    }

    [Fact]
    public async Task GetByCompanyIdAsync_ReturnsEmptyList_WhenNoBookings()
    {
        // Arrange
        _mockBookingRepository.Setup(r => r.GetByCompanyIdAsync(_testCompanyId))
            .ReturnsAsync(new List<Booking>());

        // Act
        var result = await _handler.GetByCompanyIdAsync(_testCompanyId);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetPastByUserIdAsync Tests

    [Fact]
    public async Task GetPastByUserIdAsync_ReturnsPastBookings()
    {
        // Arrange
        var pastBookings = new List<Booking>
        {
            new Booking { Id = Guid.NewGuid(), UserId = _testUserId, EndDateTime = DateTime.UtcNow.AddDays(-1) },
            new Booking { Id = Guid.NewGuid(), UserId = _testUserId, EndDateTime = DateTime.UtcNow.AddDays(-2) }
        };
        _mockBookingRepository.Setup(r => r.GetPastByUserIdAsync(_testUserId))
            .ReturnsAsync(pastBookings);

        // Act
        var result = await _handler.GetPastByUserIdAsync(_testUserId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(b => b.UserId.Should().Be(_testUserId));
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_UpdatesBooking_WhenUserOwnsIt()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var existingBooking = new Booking
        {
            Id = bookingId,
            UserId = _testUserId,
            Status = BookingStatus.Pending,
            Notes = "Old notes"
        };
        var updateData = new Booking
        {
            Notes = "Updated notes",
            StartDateTime = DateTime.UtcNow.AddDays(2)
        };

        _mockBookingRepository.Setup(r => r.GetByIdAsync(bookingId))
            .ReturnsAsync(existingBooking);
        _mockBookingRepository.Setup(r => r.UpdateAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking b) => b);

        // Act
        var result = await _handler.UpdateAsync(bookingId, updateData, _testUserId);

        // Assert
        result.Should().NotBeNull();
        result!.Notes.Should().Be("Updated notes");
        _mockBookingRepository.Verify(r => r.UpdateAsync(It.IsAny<Booking>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenBookingDoesNotExist()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        _mockBookingRepository.Setup(r => r.GetByIdAsync(bookingId))
            .ReturnsAsync((Booking?)null);

        // Act
        var result = await _handler.UpdateAsync(bookingId, new Booking(), _testUserId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenUserDoesNotOwnBooking()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var existingBooking = new Booking
        {
            Id = bookingId,
            UserId = Guid.NewGuid(), // Different user
            Status = BookingStatus.Pending
        };

        _mockBookingRepository.Setup(r => r.GetByIdAsync(bookingId))
            .ReturnsAsync(existingBooking);

        // Act
        var result = await _handler.UpdateAsync(bookingId, new Booking(), _testUserId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ThrowsException_WhenBookingNotPending()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var existingBooking = new Booking
        {
            Id = bookingId,
            UserId = _testUserId,
            Status = BookingStatus.InProgress // Changed from Confirmed - InProgress can't be updated
        };

        _mockBookingRepository.Setup(r => r.GetByIdAsync(bookingId))
            .ReturnsAsync(existingBooking);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.UpdateAsync(bookingId, new Booking(), _testUserId));
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_CreatesBooking_WithValidData()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var addressId = Guid.NewGuid();
        var serviceOptionId = Guid.NewGuid();
        
        var booking = new Booking
        {
            VehicleId = vehicleId,
            AddressId = addressId,
            ServiceOptionId = serviceOptionId,
            StartDateTime = DateTime.UtcNow.AddDays(1),
            Notes = "Test booking"
        };

        var vehicle = new Vehicle { Id = vehicleId, UserId = _testUserId };
        var address = new UserAddress { Id = addressId, UserId = _testUserId };
        var serviceOption = new ServiceOption 
        { 
            Id = serviceOptionId, 
            CompanyId = _testCompanyId,
            DurationMinutes = 30,
            Price = 25.00m
        };

        // Setup mocks
        _mockVehicleRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(vehicle);
        _mockAddressRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(address);
        _mockServiceOptionRepository.Setup(r => r.GetByIdAsync(serviceOptionId))
            .ReturnsAsync(serviceOption);
        _mockBookingRepository.Setup(r => r.HasConflictAsync(_testCompanyId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null))
            .ReturnsAsync(false);
        _mockBookingRepository.Setup(r => r.AddAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking b) => 
            {
                b.Id = Guid.NewGuid();
                return b;
            });

        // Act
        var created = await _handler.CreateAsync(booking, _testUserId);

        // Assert
        created.Should().NotBeNull();
        created.Id.Should().NotBe(Guid.Empty);
        created.UserId.Should().Be(_testUserId);
        created.CompanyId.Should().Be(_testCompanyId);
        created.Status.Should().Be(BookingStatus.Pending);
        created.EndDateTime.Should().Be(created.StartDateTime.AddMinutes(30));
        
        // Verify mocks were called
        _mockVehicleRepository.Verify(r => r.GetByIdAsync(vehicleId), Times.Once);
        _mockAddressRepository.Verify(r => r.GetByIdAsync(addressId), Times.Once);
        _mockServiceOptionRepository.Verify(r => r.GetByIdAsync(serviceOptionId), Times.Once);
        _mockBookingRepository.Verify(r => r.AddAsync(It.IsAny<Booking>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_CalculatesEndTime_BasedOnServiceDuration()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var addressId = Guid.NewGuid();
        var serviceOptionId = Guid.NewGuid();
        
        var booking = new Booking
        {
            VehicleId = vehicleId,
            AddressId = addressId,
            ServiceOptionId = serviceOptionId,
            StartDateTime = DateTime.UtcNow.AddDays(1)
        };

        _mockVehicleRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(new Vehicle { Id = vehicleId, UserId = _testUserId });
        _mockAddressRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(new UserAddress { Id = addressId, UserId = _testUserId });
        _mockServiceOptionRepository.Setup(r => r.GetByIdAsync(serviceOptionId))
            .ReturnsAsync(new ServiceOption 
            { 
                Id = serviceOptionId,
                CompanyId = _testCompanyId,
                DurationMinutes = 45,  // 45 minutes
                Price = 35.00m
            });
        _mockBookingRepository.Setup(r => r.HasConflictAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), null))
            .ReturnsAsync(false);
        _mockBookingRepository.Setup(r => r.AddAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking b) => 
            {
                b.Id = Guid.NewGuid();
                return b;
            });

        // Act
        var created = await _handler.CreateAsync(booking, _testUserId);

        // Assert
        created.EndDateTime.Should().Be(created.StartDateTime.AddMinutes(45));
    }

    [Fact]
    public async Task CreateAsync_ThrowsException_WhenVehicleDoesNotBelongToUser()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var booking = new Booking
        {
            VehicleId = vehicleId,
            AddressId = Guid.NewGuid(),
            ServiceOptionId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1)
        };

        _mockVehicleRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(new Vehicle { Id = vehicleId, UserId = Guid.NewGuid() }); // Different user!

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CreateAsync(booking, _testUserId));
        
        // Verify repository was called
        _mockVehicleRepository.Verify(r => r.GetByIdAsync(vehicleId), Times.Once);
        // Verify AddAsync was NOT called
        _mockBookingRepository.Verify(r => r.AddAsync(It.IsAny<Booking>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ThrowsException_WhenAddressDoesNotBelongToUser()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var addressId = Guid.NewGuid();
        
        var booking = new Booking
        {
            VehicleId = vehicleId,
            AddressId = addressId,
            ServiceOptionId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1)
        };

        _mockVehicleRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(new Vehicle { Id = vehicleId, UserId = _testUserId });
        _mockAddressRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(new UserAddress { Id = addressId, UserId = Guid.NewGuid() }); // Different user!

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CreateAsync(booking, _testUserId));
    }

    [Fact]
    public async Task CreateAsync_ThrowsException_WhenTimeSlotConflicts()
    {
        // Arrange
        var vehicleId = Guid.NewGuid();
        var addressId = Guid.NewGuid();
        var serviceOptionId = Guid.NewGuid();
        
        var booking = new Booking
        {
            VehicleId = vehicleId,
            AddressId = addressId,
            ServiceOptionId = serviceOptionId,
            StartDateTime = DateTime.UtcNow.AddDays(1)
        };

        _mockVehicleRepository.Setup(r => r.GetByIdAsync(vehicleId))
            .ReturnsAsync(new Vehicle { Id = vehicleId, UserId = _testUserId });
        _mockAddressRepository.Setup(r => r.GetByIdAsync(addressId))
            .ReturnsAsync(new UserAddress { Id = addressId, UserId = _testUserId });
        _mockServiceOptionRepository.Setup(r => r.GetByIdAsync(serviceOptionId))
            .ReturnsAsync(new ServiceOption 
            { 
                Id = serviceOptionId,
                CompanyId = _testCompanyId,
                DurationMinutes = 30
            });
        _mockBookingRepository.Setup(r => r.HasConflictAsync(_testCompanyId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null))
            .ReturnsAsync(true); // Conflict exists!

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CreateAsync(booking, _testUserId));
    }

    #endregion

    #region CancelAsync Tests

    [Fact]
    public async Task CancelAsync_ReturnsFalse_WhenBookingDoesNotExist()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        _mockBookingRepository.Setup(r => r.GetByIdAsync(bookingId))
            .ReturnsAsync((Booking?)null);

        // Act
        var result = await _handler.CancelAsync(bookingId, _testUserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CancelAsync_ReturnsFalse_WhenUserDoesNotOwnBooking()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var booking = new Booking
        {
            Id = bookingId,
            UserId = Guid.NewGuid(), // Different user
            Status = BookingStatus.Pending
        };
        
        _mockBookingRepository.Setup(r => r.GetByIdAsync(bookingId))
            .ReturnsAsync(booking);

        // Act
        var result = await _handler.CancelAsync(bookingId, _testUserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CancelAsync_CancelsBooking_WhenValid()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var booking = new Booking
        {
            Id = bookingId,
            UserId = _testUserId,
            Status = BookingStatus.Pending
        };
        
        _mockBookingRepository.Setup(r => r.GetByIdAsync(bookingId))
            .ReturnsAsync(booking);
        _mockBookingRepository.Setup(r => r.UpdateAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking b) => b);

        // Act
        var result = await _handler.CancelAsync(bookingId, _testUserId);

        // Assert
        result.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Cancelled);
        _mockBookingRepository.Verify(r => r.UpdateAsync(It.Is<Booking>(b => b.Status == BookingStatus.Cancelled)), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_ThrowsException_WhenBookingAlreadyCompleted()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var booking = new Booking
        {
            Id = bookingId,
            UserId = _testUserId,
            Status = BookingStatus.Completed // Already completed
        };
        
        _mockBookingRepository.Setup(r => r.GetByIdAsync(bookingId))
            .ReturnsAsync(booking);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CancelAsync(bookingId, _testUserId));
    }

    #endregion

    #region ConfirmAsync Tests

    [Fact]
    public async Task ConfirmAsync_ReturnsFalse_WhenBookingDoesNotExist()
    {
        // Arrange
        _mockBookingRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Booking?)null);

        // Act
        var result = await _handler.ConfirmAsync(Guid.NewGuid(), _testCompanyId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmAsync_ReturnsFalse_WhenCompanyDoesNotOwnBooking()
    {
        // Arrange
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(), // Different company
            Status = BookingStatus.Pending
        };
        
        _mockBookingRepository.Setup(r => r.GetByIdAsync(booking.Id))
            .ReturnsAsync(booking);

        // Act
        var result = await _handler.ConfirmAsync(booking.Id, _testCompanyId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmAsync_ConfirmsBooking_WhenPending()
    {
        // Arrange
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CompanyId = _testCompanyId,
            Status = BookingStatus.Pending
        };
        
        _mockBookingRepository.Setup(r => r.GetByIdAsync(booking.Id))
            .ReturnsAsync(booking);
        _mockBookingRepository.Setup(r => r.UpdateAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking b) => b);

        // Act
        var result = await _handler.ConfirmAsync(booking.Id, _testCompanyId);

        // Assert
        result.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Confirmed);
    }

    [Fact]
    public async Task ConfirmAsync_ThrowsException_WhenBookingNotPending()
    {
        // Arrange
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CompanyId = _testCompanyId,
            Status = BookingStatus.Confirmed // Not pending
        };
        
        _mockBookingRepository.Setup(r => r.GetByIdAsync(booking.Id))
            .ReturnsAsync(booking);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.ConfirmAsync(booking.Id, _testCompanyId));
    }

    #endregion

    #region StartServiceAsync Tests

    [Fact]
    public async Task StartServiceAsync_StartsService_WhenConfirmed()
    {
        // Arrange
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CompanyId = _testCompanyId,
            Status = BookingStatus.Confirmed
        };
        
        _mockBookingRepository.Setup(r => r.GetByIdAsync(booking.Id))
            .ReturnsAsync(booking);
        _mockBookingRepository.Setup(r => r.UpdateAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking b) => b);

        // Act
        var result = await _handler.StartServiceAsync(booking.Id, _testCompanyId);

        // Assert
        result.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.InProgress);
    }

    [Fact]
    public async Task StartServiceAsync_ThrowsException_WhenNotConfirmed()
    {
        // Arrange
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CompanyId = _testCompanyId,
            Status = BookingStatus.Pending
        };
        
        _mockBookingRepository.Setup(r => r.GetByIdAsync(booking.Id))
            .ReturnsAsync(booking);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.StartServiceAsync(booking.Id, _testCompanyId));
    }

    #endregion

    #region CompleteServiceAsync Tests

    [Fact]
    public async Task CompleteServiceAsync_CompletesService_WhenInProgress()
    {
        // Arrange
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CompanyId = _testCompanyId,
            Status = BookingStatus.InProgress
        };
        
        _mockBookingRepository.Setup(r => r.GetByIdAsync(booking.Id))
            .ReturnsAsync(booking);
        _mockBookingRepository.Setup(r => r.UpdateAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking b) => b);

        // Act
        var result = await _handler.CompleteServiceAsync(booking.Id, _testCompanyId);

        // Assert
        result.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Completed);
    }

    [Fact]
    public async Task CompleteServiceAsync_ThrowsException_WhenNotInProgress()
    {
        // Arrange
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            CompanyId = _testCompanyId,
            Status = BookingStatus.Pending
        };
        
        _mockBookingRepository.Setup(r => r.GetByIdAsync(booking.Id))
            .ReturnsAsync(booking);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CompleteServiceAsync(booking.Id, _testCompanyId));
    }

    #endregion

    #region IsTimeSlotAvailableAsync Tests

    [Fact]
    public async Task IsTimeSlotAvailableAsync_ReturnsTrue_WhenNoConflicts()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddDays(1);
        var endTime = startTime.AddHours(1);
        
        _mockBookingRepository.Setup(r => r.HasConflictAsync(_testCompanyId, startTime, endTime, null))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.IsTimeSlotAvailableAsync(_testCompanyId, startTime, endTime);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsTimeSlotAvailableAsync_ReturnsFalse_WhenConflictExists()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddDays(1);
        var endTime = startTime.AddHours(1);
        
        _mockBookingRepository.Setup(r => r.HasConflictAsync(_testCompanyId, startTime, endTime, null))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.IsTimeSlotAvailableAsync(_testCompanyId, startTime, endTime);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsTimeSlotAvailableAsync_ExcludesSpecifiedBooking()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddDays(1);
        var endTime = startTime.AddHours(1);
        var excludeBookingId = Guid.NewGuid();
        
        _mockBookingRepository.Setup(r => r.HasConflictAsync(_testCompanyId, startTime, endTime, excludeBookingId))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.IsTimeSlotAvailableAsync(_testCompanyId, startTime, endTime, excludeBookingId);

        // Assert
        result.Should().BeTrue();
        _mockBookingRepository.Verify(r => r.HasConflictAsync(_testCompanyId, startTime, endTime, excludeBookingId), Times.Once);
    }

    #endregion

    #region GetByUserIdAsync Tests

    [Fact]
    public async Task GetByUserIdAsync_ReturnsUserBookings()
    {
        // Arrange
        var bookings = new List<Booking>
        {
            new Booking { Id = Guid.NewGuid(), UserId = _testUserId },
            new Booking { Id = Guid.NewGuid(), UserId = _testUserId }
        };
        
        _mockBookingRepository.Setup(r => r.GetByUserIdAsync(_testUserId))
            .ReturnsAsync(bookings);

        // Act
        var result = await _handler.GetByUserIdAsync(_testUserId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(b => b.UserId.Should().Be(_testUserId));
    }

    #endregion

    #region GetUpcomingByUserIdAsync Tests

    [Fact]
    public async Task GetUpcomingByUserIdAsync_ReturnsUpcomingBookings()
    {
        // Arrange
        var upcomingBookings = new List<Booking>
        {
            new Booking { Id = Guid.NewGuid(), UserId = _testUserId, StartDateTime = DateTime.UtcNow.AddDays(1) },
            new Booking { Id = Guid.NewGuid(), UserId = _testUserId, StartDateTime = DateTime.UtcNow.AddDays(2) }
        };
        _mockBookingRepository.Setup(r => r.GetUpcomingByUserIdAsync(_testUserId))
            .ReturnsAsync(upcomingBookings);

        // Act
        var result = await _handler.GetUpcomingByUserIdAsync(_testUserId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(b => b.UserId.Should().Be(_testUserId));
    }

    #endregion
}
