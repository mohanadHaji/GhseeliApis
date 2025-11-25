using FluentAssertions;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Payment;
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
/// Unit tests for PaymentsController
/// </summary>
public class PaymentsControllerTests
{
    private readonly Mock<IPaymentHandler> _mockPaymentHandler;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly PaymentsController _controller;
    private readonly Guid _testUserId;

    public PaymentsControllerTests()
    {
        _mockPaymentHandler = new Mock<IPaymentHandler>();
        _mockLogger = new Mock<IAppLogger>();
        _controller = new PaymentsController(_mockPaymentHandler.Object, _mockLogger.Object);
        _testUserId = Guid.NewGuid();

        SetupAuthenticatedUser(_testUserId);
    }

    private void SetupAuthenticatedUser(Guid userId, string role = "User")
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };
    }

    private static Payment CreateTestPayment(Guid? userId = null, Guid? bookingId = null)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId ?? Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            Amount = 100.00m,
            Method = PaymentMethod.Card,
            Status = PaymentStatus.Pending,
            TransactionId = "TXN123456",
            CreatedAt = DateTime.UtcNow
        };
    }

    #region GetAll Tests (Admin Only)

    [Fact]
    public async Task GetAll_ReturnsOk_WithAllPayments_WhenUserIsAdmin()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var payments = new List<Payment>
        {
            CreateTestPayment(),
            CreateTestPayment()
        };

        _mockPaymentHandler.Setup(h => h.GetAllAsync())
            .ReturnsAsync(payments);

        // Act
        var result = await _controller.GetAll();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<PaymentResponse>>().Subject;
        response.Should().HaveCount(2);

        _mockPaymentHandler.Verify(h => h.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task GetAll_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        _mockPaymentHandler.Setup(h => h.GetAllAsync())
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetAll();

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region GetById Tests

    [Fact]
    public async Task GetById_ReturnsOk_WithPayment_WhenPaymentExists()
    {
        // Arrange
        var payment = CreateTestPayment(_testUserId);
        _mockPaymentHandler.Setup(h => h.GetByIdAsync(payment.Id))
            .ReturnsAsync(payment);

        // Act
        var result = await _controller.GetById(payment.Id);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<PaymentResponse>().Subject;
        response.Id.Should().Be(payment.Id);
        response.Amount.Should().Be(payment.Amount);

        _mockPaymentHandler.Verify(h => h.GetByIdAsync(payment.Id), Times.Once);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenPaymentDoesNotExist()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        _mockPaymentHandler.Setup(h => h.GetByIdAsync(paymentId))
            .ReturnsAsync((Payment?)null);

        // Act
        var result = await _controller.GetById(paymentId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();

        _mockPaymentHandler.Verify(h => h.GetByIdAsync(paymentId), Times.Once);
    }

    [Fact]
    public async Task GetById_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        _mockPaymentHandler.Setup(h => h.GetByIdAsync(paymentId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetById(paymentId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region GetMyPayments Tests

    [Fact]
    public async Task GetMyPayments_ReturnsOk_WithUserPayments()
    {
        // Arrange
        var payments = new List<Payment>
        {
            CreateTestPayment(_testUserId),
            CreateTestPayment(_testUserId)
        };

        _mockPaymentHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ReturnsAsync(payments);

        // Act
        var result = await _controller.GetMyPayments();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<PaymentResponse>>().Subject;
        response.Should().HaveCount(2);

        _mockPaymentHandler.Verify(h => h.GetByUserIdAsync(_testUserId), Times.Once);
    }

    [Fact]
    public async Task GetMyPayments_ReturnsEmptyList_WhenNoPaymentsExist()
    {
        // Arrange
        _mockPaymentHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ReturnsAsync(new List<Payment>());

        // Act
        var result = await _controller.GetMyPayments();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<PaymentResponse>>().Subject;
        response.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyPayments_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        _mockPaymentHandler.Setup(h => h.GetByUserIdAsync(_testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetMyPayments();

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region GetByBookingId Tests

    [Fact]
    public async Task GetByBookingId_ReturnsOk_WithPayment_WhenPaymentExists()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var payment = CreateTestPayment(_testUserId, bookingId);
        _mockPaymentHandler.Setup(h => h.GetByBookingIdAsync(bookingId))
            .ReturnsAsync(payment);

        // Act
        var result = await _controller.GetByBookingId(bookingId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<PaymentResponse>().Subject;
        response.BookingId.Should().Be(bookingId);

        _mockPaymentHandler.Verify(h => h.GetByBookingIdAsync(bookingId), Times.Once);
    }

    [Fact]
    public async Task GetByBookingId_ReturnsNotFound_WhenPaymentDoesNotExist()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        _mockPaymentHandler.Setup(h => h.GetByBookingIdAsync(bookingId))
            .ReturnsAsync((Payment?)null);

        // Act
        var result = await _controller.GetByBookingId(bookingId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetByBookingId_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        _mockPaymentHandler.Setup(h => h.GetByBookingIdAsync(bookingId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetByBookingId(bookingId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region Create Tests

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenPaymentIsCreatedSuccessfully()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        var request = new CreatePaymentRequest
        {
            BookingId = bookingId,
            Amount = 150.00m,
            Method = PaymentMethod.Card,
            TransactionId = "TXN789"
        };

        var createdPayment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = request.BookingId,
            UserId = _testUserId,
            Amount = request.Amount,
            Method = request.Method,
            Status = PaymentStatus.Pending,
            TransactionId = request.TransactionId,
            CreatedAt = DateTime.UtcNow
        };

        _mockPaymentHandler.Setup(h => h.CreateAsync(It.IsAny<Payment>(), _testUserId))
            .ReturnsAsync(createdPayment);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(PaymentsController.GetById));
        
        var response = createdResult.Value.Should().BeOfType<PaymentResponse>().Subject;
        response.Amount.Should().Be(request.Amount);
        response.BookingId.Should().Be(bookingId);

        _mockPaymentHandler.Verify(h => h.CreateAsync(It.IsAny<Payment>(), _testUserId), Times.Once);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        var request = new CreatePaymentRequest
        {
            BookingId = Guid.NewGuid(),
            Amount = 100.00m,
            Method = PaymentMethod.Card
        };

        _mockPaymentHandler.Setup(h => h.CreateAsync(It.IsAny<Payment>(), _testUserId))
            .ThrowsAsync(new InvalidOperationException("Payment already exists"));

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
        var request = new CreatePaymentRequest
        {
            BookingId = Guid.NewGuid(),
            Amount = 100.00m,
            Method = PaymentMethod.Card
        };

        _mockPaymentHandler.Setup(h => h.CreateAsync(It.IsAny<Payment>(), _testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Create(request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region UpdateStatus Tests (Admin Only)

    [Fact]
    public async Task UpdateStatus_ReturnsOk_WhenStatusIsUpdatedSuccessfully()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var paymentId = Guid.NewGuid();
        var request = new UpdatePaymentStatusRequest
        {
            Status = PaymentStatus.Completed
        };

        var updatedPayment = CreateTestPayment(_testUserId);
        updatedPayment.Id = paymentId;
        updatedPayment.Status = PaymentStatus.Completed;

        _mockPaymentHandler.Setup(h => h.UpdateStatusAsync(paymentId, request.Status))
            .ReturnsAsync(updatedPayment);

        // Act
        var result = await _controller.UpdateStatus(paymentId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<PaymentResponse>().Subject;
        response.Status.Should().Be(PaymentStatus.Completed);

        _mockPaymentHandler.Verify(h => h.UpdateStatusAsync(paymentId, request.Status), Times.Once);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsNotFound_WhenPaymentDoesNotExist()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var paymentId = Guid.NewGuid();
        var request = new UpdatePaymentStatusRequest
        {
            Status = PaymentStatus.Completed
        };

        _mockPaymentHandler.Setup(h => h.UpdateStatusAsync(paymentId, request.Status))
            .ReturnsAsync((Payment?)null);

        // Act
        var result = await _controller.UpdateStatus(paymentId, request);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateStatus_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var paymentId = Guid.NewGuid();
        var request = new UpdatePaymentStatusRequest
        {
            Status = PaymentStatus.Completed
        };

        _mockPaymentHandler.Setup(h => h.UpdateStatusAsync(paymentId, request.Status))
            .ThrowsAsync(new InvalidOperationException("Cannot update status"));

        // Act
        var result = await _controller.UpdateStatus(paymentId, request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();

        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        SetupAuthenticatedUser(_testUserId, "Admin");
        var paymentId = Guid.NewGuid();
        var request = new UpdatePaymentStatusRequest
        {
            Status = PaymentStatus.Completed
        };

        _mockPaymentHandler.Setup(h => h.UpdateStatusAsync(paymentId, request.Status))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.UpdateStatus(paymentId, request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region Refund Tests

    [Fact]
    public async Task Refund_ReturnsOk_WhenRefundIsProcessedSuccessfully()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var refundedPayment = CreateTestPayment(_testUserId);
        refundedPayment.Id = paymentId;
        refundedPayment.Status = PaymentStatus.Refunded;

        _mockPaymentHandler.Setup(h => h.ProcessRefundAsync(paymentId, _testUserId))
            .ReturnsAsync(refundedPayment);

        // Act
        var result = await _controller.Refund(paymentId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<PaymentResponse>().Subject;
        response.Status.Should().Be(PaymentStatus.Refunded);

        _mockPaymentHandler.Verify(h => h.ProcessRefundAsync(paymentId, _testUserId), Times.Once);
    }

    [Fact]
    public async Task Refund_ReturnsNotFound_WhenPaymentDoesNotExist()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        _mockPaymentHandler.Setup(h => h.ProcessRefundAsync(paymentId, _testUserId))
            .ReturnsAsync((Payment?)null);

        // Act
        var result = await _controller.Refund(paymentId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Refund_ReturnsBadRequest_WhenInvalidOperationExceptionOccurs()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        _mockPaymentHandler.Setup(h => h.ProcessRefundAsync(paymentId, _testUserId))
            .ThrowsAsync(new InvalidOperationException("Cannot refund completed payment"));

        // Act
        var result = await _controller.Refund(paymentId);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();

        _mockLogger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Refund_ReturnsInternalServerError_WhenExceptionOccurs()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        _mockPaymentHandler.Setup(h => h.ProcessRefundAsync(paymentId, _testUserId))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Refund(paymentId);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);

        _mockLogger.Verify(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion
}
