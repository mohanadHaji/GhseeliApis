using FluentAssertions;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Booking;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Unit tests for BookingsController
/// </summary>
public class BookingsControllerTests
{
    private readonly Mock<IBookingHandler> _mockBookingHandler;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly BookingsController _controller;
    private readonly Guid _testUserId;
    private readonly Guid _testCompanyId;

    public BookingsControllerTests()
    {
        _mockBookingHandler = new Mock<IBookingHandler>();
        _mockLogger = new Mock<IAppLogger>();
        _controller = new BookingsController(_mockBookingHandler.Object, _mockLogger.Object);
        _testUserId = Guid.NewGuid();
        _testCompanyId = Guid.NewGuid();

        SetupAuthenticatedUser(_testUserId);
    }

    private void SetupAuthenticatedUser(Guid userId, string role = "User", Guid? companyId = null)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Role, role)
        };

        if (companyId.HasValue)
        {
            claims.Add(new Claim("CompanyId", companyId.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };
    }

    private static Booking CreateTestBooking(Guid? userId = null, Guid? companyId = null)
    {
        return new Booking
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            CompanyId = companyId ?? Guid.NewGuid(),
            ServiceOptionId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1),
            EndDateTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            Status = BookingStatus.Pending,
            IsPaid = false
        };
    }

    #region GetMyBookings Tests

    [Fact]
    public async Task GetMyBookings_ReturnsOk_WithBookings()
    {
        // Arrange
        var bookings = new List<Booking>
        {
            CreateTestBooking(_testUserId),
            CreateTestBooking(_testUserId)
        };

        _mockBookingHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ReturnsAsync(bookings);

        // Act
        var result = await _controller.GetMyBookings();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<BookingResponse>>().Subject;
        response.Should().HaveCount(2);

        _mockBookingHandler.Verify(h => h.GetByUserIdAsync(_testUserId), Times.Once);
    }

    [Fact]
    public async Task GetMyBookings_ReturnsEmptyList_WhenNoBookingsExist()
    {
        // Arrange
        _mockBookingHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ReturnsAsync(new List<Booking>());

        // Act
        var result = await _controller.GetMyBookings();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<BookingResponse>>().Subject;
        response.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyBookings_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        _mockBookingHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetMyBookings();

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region GetUpcomingBookings Tests

    [Fact]
    public async Task GetUpcomingBookings_ReturnsOk_WithUpcomingBookings()
    {
        // Arrange
        var bookings = new List<Booking>
        {
            CreateTestBooking(_testUserId)
        };
        bookings[0].StartDateTime = DateTime.UtcNow.AddDays(2);

        _mockBookingHandler.Setup(h => h.GetUpcomingByUserIdAsync(_testUserId))
            .ReturnsAsync(bookings);

        // Act
        var result = await _controller.GetUpcomingBookings();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<BookingResponse>>().Subject;
        response.Should().HaveCount(1);

        _mockBookingHandler.Verify(h => h.GetUpcomingByUserIdAsync(_testUserId), Times.Once);
    }

    [Fact]
    public async Task GetUpcomingBookings_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        _mockBookingHandler.Setup(h => h.GetUpcomingByUserIdAsync(_testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetUpcomingBookings();

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region GetPastBookings Tests

    [Fact]
    public async Task GetPastBookings_ReturnsOk_WithPastBookings()
    {
        // Arrange
        var bookings = new List<Booking>
        {
            CreateTestBooking(_testUserId)
        };
        bookings[0].StartDateTime = DateTime.UtcNow.AddDays(-2);
        bookings[0].Status = BookingStatus.Completed;

        _mockBookingHandler.Setup(h => h.GetPastByUserIdAsync(_testUserId))
            .ReturnsAsync(bookings);

        // Act
        var result = await _controller.GetPastBookings();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<BookingResponse>>().Subject;
        response.Should().HaveCount(1);

        _mockBookingHandler.Verify(h => h.GetPastByUserIdAsync(_testUserId), Times.Once);
    }

    [Fact]
    public async Task GetPastBookings_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        _mockBookingHandler.Setup(h => h.GetPastByUserIdAsync(_testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetPastBookings();

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region GetCompanyBookings Tests

    [Fact]
    public async Task GetCompanyBookings_ReturnsOk_WithBookings()
    {
        // Arrange
        var bookings = new List<Booking>
        {
            CreateTestBooking(companyId: _testCompanyId),
            CreateTestBooking(companyId: _testCompanyId)
        };

        _mockBookingHandler.Setup(h => h.GetByCompanyIdAsync(_testCompanyId))
            .ReturnsAsync(bookings);

        // Act
        var result = await _controller.GetCompanyBookings(_testCompanyId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<BookingResponse>>().Subject;
        response.Should().HaveCount(2);

        _mockBookingHandler.Verify(h => h.GetByCompanyIdAsync(_testCompanyId), Times.Once);
    }

    [Fact]
    public async Task GetCompanyBookings_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        _mockBookingHandler.Setup(h => h.GetByCompanyIdAsync(_testCompanyId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetCompanyBookings(_testCompanyId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_ReturnsOk_WithBooking_WhenBookingExists()
    {
        // Arrange
        var booking = CreateTestBooking(_testUserId);
        _mockBookingHandler.Setup(h => h.GetByIdAsync(booking.Id))
            .ReturnsAsync(booking);

        // Act
        var result = await _controller.GetById(booking.Id);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<BookingResponse>().Subject;
        response.Id.Should().Be(booking.Id);

        _mockBookingHandler.Verify(h => h.GetByIdAsync(booking.Id), Times.Once);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenBookingDoesNotExist()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        _mockBookingHandler.Setup(h => h.GetByIdAsync(bookingId))
            .ReturnsAsync((Booking?)null);

        // Act
        var result = await _controller.GetById(bookingId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetById_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        _mockBookingHandler.Setup(h => h.GetByIdAsync(bookingId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetById(bookingId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region Create Tests

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenBookingIsCreatedSuccessfully()
    {
        // Arrange
        var request = new CreateBookingRequest
        {
            VehicleId = Guid.NewGuid(),
            ServiceOptionId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1),
            Notes = "Test booking"
        };

        var createdBooking = CreateTestBooking(_testUserId);

        _mockBookingHandler.Setup(h => h.CreateAsync(It.IsAny<Booking>(), _testUserId))
            .ReturnsAsync(createdBooking);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(BookingsController.GetById));

        _mockBookingHandler.Verify(h => h.CreateAsync(It.IsAny<Booking>(), _testUserId), Times.Once);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        var request = new CreateBookingRequest
        {
            VehicleId = Guid.NewGuid(),
            ServiceOptionId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1)
        };

        _mockBookingHandler.Setup(h => h.CreateAsync(It.IsAny<Booking>(), _testUserId))
            .ThrowsAsync(new InvalidOperationException("Time slot not available"));

        // Act
        var result = await _controller.Create(request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();

        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Create_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var request = new CreateBookingRequest
        {
            VehicleId = Guid.NewGuid(),
            ServiceOptionId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1)
        };

        _mockBookingHandler.Setup(h => h.CreateAsync(It.IsAny<Booking>(), _testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Create(request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task Update_ReturnsOk_WhenBookingIsUpdatedSuccessfully()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var request = new UpdateBookingRequest
        {
            StartDateTime = DateTime.UtcNow.AddDays(2),
            Notes = "Updated notes"
        };

        var updatedBooking = CreateTestBooking(_testUserId);
        updatedBooking.Id = bookingId;

        _mockBookingHandler.Setup(h => h.UpdateAsync(bookingId, It.IsAny<Booking>(), _testUserId))
            .ReturnsAsync(updatedBooking);

        // Act
        var result = await _controller.Update(bookingId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<BookingResponse>().Subject;
        response.Id.Should().Be(bookingId);

        _mockBookingHandler.Verify(h => h.UpdateAsync(bookingId, It.IsAny<Booking>(), _testUserId), Times.Once);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenBookingDoesNotExist()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var request = new UpdateBookingRequest
        {
            StartDateTime = DateTime.UtcNow.AddDays(2)
        };

        _mockBookingHandler.Setup(h => h.UpdateAsync(bookingId, It.IsAny<Booking>(), _testUserId))
            .ReturnsAsync((Booking?)null);

        // Act
        var result = await _controller.Update(bookingId, request);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Update_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var request = new UpdateBookingRequest
        {
            StartDateTime = DateTime.UtcNow.AddDays(2)
        };

        _mockBookingHandler.Setup(h => h.UpdateAsync(bookingId, It.IsAny<Booking>(), _testUserId))
            .ThrowsAsync(new InvalidOperationException("Cannot update confirmed booking"));

        // Act
        var result = await _controller.Update(bookingId, request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();
    }

    #endregion

    #region Cancel Tests

    [Fact]
    public async Task Cancel_ReturnsOk_WhenBookingIsCancelledSuccessfully()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        _mockBookingHandler.Setup(h => h.CancelAsync(bookingId, _testUserId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Cancel(bookingId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();

        _mockBookingHandler.Verify(h => h.CancelAsync(bookingId, _testUserId), Times.Once);
    }

    [Fact]
    public async Task Cancel_ReturnsNotFound_WhenBookingDoesNotExist()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        _mockBookingHandler.Setup(h => h.CancelAsync(bookingId, _testUserId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.Cancel(bookingId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Cancel_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        _mockBookingHandler.Setup(h => h.CancelAsync(bookingId, _testUserId))
            .ThrowsAsync(new InvalidOperationException("Cannot cancel completed booking"));

        // Act
        var result = await _controller.Cancel(bookingId);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();
    }

    #endregion

    #region Confirm Tests (Company Role)

    [Fact]
    public async Task Confirm_ReturnsOk_WhenBookingIsConfirmedSuccessfully()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company", _testCompanyId);
        var bookingId = Guid.NewGuid();

        _mockBookingHandler.Setup(h => h.ConfirmAsync(bookingId, _testCompanyId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Confirm(bookingId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();

        _mockBookingHandler.Verify(h => h.ConfirmAsync(bookingId, _testCompanyId), Times.Once);
    }

    [Fact]
    public async Task Confirm_ReturnsBadRequest_WhenCompanyIdClaimIsMissing()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company"); // No CompanyId claim
        var bookingId = Guid.NewGuid();

        // Act
        var result = await _controller.Confirm(bookingId);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();

        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
        _mockBookingHandler.Verify(h => h.ConfirmAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_ReturnsNotFound_WhenBookingDoesNotExist()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company", _testCompanyId);
        var bookingId = Guid.NewGuid();

        _mockBookingHandler.Setup(h => h.ConfirmAsync(bookingId, _testCompanyId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.Confirm(bookingId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Confirm_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company", _testCompanyId);
        var bookingId = Guid.NewGuid();

        _mockBookingHandler.Setup(h => h.ConfirmAsync(bookingId, _testCompanyId))
            .ThrowsAsync(new InvalidOperationException("Booking already confirmed"));

        // Act
        var result = await _controller.Confirm(bookingId);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();
    }

    #endregion

    #region StartService Tests (Company Role)

    [Fact]
    public async Task StartService_ReturnsOk_WhenServiceIsStartedSuccessfully()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company", _testCompanyId);
        var bookingId = Guid.NewGuid();

        _mockBookingHandler.Setup(h => h.StartServiceAsync(bookingId, _testCompanyId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.StartService(bookingId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();

        _mockBookingHandler.Verify(h => h.StartServiceAsync(bookingId, _testCompanyId), Times.Once);
    }

    [Fact]
    public async Task StartService_ReturnsBadRequest_WhenCompanyIdClaimIsMissing()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company"); // No CompanyId claim
        var bookingId = Guid.NewGuid();

        // Act
        var result = await _controller.StartService(bookingId);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();

        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task StartService_ReturnsNotFound_WhenBookingDoesNotExist()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company", _testCompanyId);
        var bookingId = Guid.NewGuid();

        _mockBookingHandler.Setup(h => h.StartServiceAsync(bookingId, _testCompanyId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.StartService(bookingId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    #endregion

    #region CompleteService Tests (Company Role)

    [Fact]
    public async Task CompleteService_ReturnsOk_WhenServiceIsCompletedSuccessfully()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company", _testCompanyId);
        var bookingId = Guid.NewGuid();

        _mockBookingHandler.Setup(h => h.CompleteServiceAsync(bookingId, _testCompanyId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.CompleteService(bookingId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();

        _mockBookingHandler.Verify(h => h.CompleteServiceAsync(bookingId, _testCompanyId), Times.Once);
    }

    [Fact]
    public async Task CompleteService_ReturnsBadRequest_WhenCompanyIdClaimIsMissing()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company"); // No CompanyId claim
        var bookingId = Guid.NewGuid();

        // Act
        var result = await _controller.CompleteService(bookingId);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();

        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CompleteService_ReturnsNotFound_WhenBookingDoesNotExist()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Company", _testCompanyId);
        var bookingId = Guid.NewGuid();

        _mockBookingHandler.Setup(h => h.CompleteServiceAsync(bookingId, _testCompanyId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.CompleteService(bookingId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    #endregion

    #region CheckAvailability Tests

    [Fact]
    public async Task CheckAvailability_ReturnsOk_WithAvailabilityStatus()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var startTime = DateTime.UtcNow.AddDays(1);
        var endTime = startTime.AddHours(2);

        _mockBookingHandler.Setup(h => h.IsTimeSlotAvailableAsync(companyId, startTime, endTime, null))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.CheckAvailability(companyId, startTime, endTime);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();

        _mockBookingHandler.Verify(h => h.IsTimeSlotAvailableAsync(companyId, startTime, endTime, null), Times.Once);
    }

    [Fact]
    public async Task CheckAvailability_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var startTime = DateTime.UtcNow.AddDays(1);
        var endTime = startTime.AddHours(2);

        _mockBookingHandler.Setup(h => h.IsTimeSlotAvailableAsync(companyId, startTime, endTime, null))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.CheckAvailability(companyId, startTime, endTime);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion
}
