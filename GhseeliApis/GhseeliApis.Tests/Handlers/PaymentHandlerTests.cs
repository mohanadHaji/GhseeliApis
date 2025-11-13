using FluentAssertions;
using GhseeliApis.Handlers;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Repositories.Interfaces;
using Moq;

namespace GhseeliApis.Tests.Handlers;

/// <summary>
/// Unit tests for PaymentHandler using Moq
/// </summary>
public class PaymentHandlerTests
{
    private readonly Mock<IPaymentRepository> _mockPaymentRepository;
    private readonly Mock<IBookingRepository> _mockBookingRepository;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly PaymentHandler _handler;
    private readonly Guid _testUserId;
    private readonly Guid _testBookingId;

    public PaymentHandlerTests()
    {
        _mockPaymentRepository = new Mock<IPaymentRepository>();
        _mockBookingRepository = new Mock<IBookingRepository>();
        _mockLogger = new Mock<IAppLogger>();
        
        _handler = new PaymentHandler(
            _mockPaymentRepository.Object,
            _mockBookingRepository.Object,
            _mockLogger.Object);
        
        _testUserId = Guid.NewGuid();
        _testBookingId = Guid.NewGuid();
    }

    #region GetAllAsync Tests

    [Fact]
    public async Task GetAllAsync_ReturnsAllPayments()
    {
        // Arrange
        var payments = new List<Payment>
        {
            new Payment { Id = Guid.NewGuid(), UserId = _testUserId, Amount = 25m },
            new Payment { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Amount = 50m }
        };
        _mockPaymentRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(payments);

        // Act
        var result = await _handler.GetAllAsync();

        // Assert
        result.Should().HaveCount(2);
        _mockPaymentRepository.Verify(r => r.GetAllAsync(), Times.Once);
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_ReturnsPayment_WhenExists()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var payment = new Payment { Id = paymentId, UserId = _testUserId, Amount = 25m };
        _mockPaymentRepository.Setup(r => r.GetByIdAsync(paymentId)).ReturnsAsync(payment);

        // Act
        var result = await _handler.GetByIdAsync(paymentId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(paymentId);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        _mockPaymentRepository.Setup(r => r.GetByIdAsync(paymentId)).ReturnsAsync((Payment?)null);

        // Act
        var result = await _handler.GetByIdAsync(paymentId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetByUserIdAsync Tests

    [Fact]
    public async Task GetByUserIdAsync_ReturnsUserPayments()
    {
        // Arrange
        var payments = new List<Payment>
        {
            new Payment { Id = Guid.NewGuid(), UserId = _testUserId, Amount = 25m },
            new Payment { Id = Guid.NewGuid(), UserId = _testUserId, Amount = 50m }
        };
        _mockPaymentRepository.Setup(r => r.GetByUserIdAsync(_testUserId)).ReturnsAsync(payments);

        // Act
        var result = await _handler.GetByUserIdAsync(_testUserId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(p => p.UserId.Should().Be(_testUserId));
    }

    #endregion

    #region GetByBookingIdAsync Tests

    [Fact]
    public async Task GetByBookingIdAsync_ReturnsPayment_WhenExists()
    {
        // Arrange
        var payment = new Payment { Id = Guid.NewGuid(), BookingId = _testBookingId, Amount = 25m };
        _mockPaymentRepository.Setup(r => r.GetByBookingIdAsync(_testBookingId)).ReturnsAsync(payment);

        // Act
        var result = await _handler.GetByBookingIdAsync(_testBookingId);

        // Assert
        result.Should().NotBeNull();
        result!.BookingId.Should().Be(_testBookingId);
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_CreatesPayment_WithValidData()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = _testBookingId,
            Amount = 25.50m,
            Method = PaymentMethod.Card
        };

        var booking = new Booking { Id = _testBookingId, UserId = _testUserId };

        _mockBookingRepository.Setup(r => r.GetByIdAsync(_testBookingId)).ReturnsAsync(booking);
        _mockPaymentRepository.Setup(r => r.GetByBookingIdAsync(_testBookingId)).ReturnsAsync((Payment?)null);
        _mockPaymentRepository.Setup(r => r.AddAsync(It.IsAny<Payment>()))
            .ReturnsAsync((Payment p) => 
            {
                p.Id = Guid.NewGuid();
                return p;
            });

        // Act
        var created = await _handler.CreateAsync(payment, _testUserId);

        // Assert
        created.Should().NotBeNull();
        created.UserId.Should().Be(_testUserId);
        created.Status.Should().Be(PaymentStatus.Pending);
        created.Amount.Should().Be(25.50m);
        _mockPaymentRepository.Verify(r => r.AddAsync(It.IsAny<Payment>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ThrowsException_WhenBookingNotFound()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = _testBookingId,
            Amount = 25m,
            Method = PaymentMethod.Card
        };

        _mockBookingRepository.Setup(r => r.GetByIdAsync(_testBookingId)).ReturnsAsync((Booking?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CreateAsync(payment, _testUserId));
        
        _mockPaymentRepository.Verify(r => r.AddAsync(It.IsAny<Payment>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ThrowsException_WhenUserDoesNotOwnBooking()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = _testBookingId,
            Amount = 25m,
            Method = PaymentMethod.Card
        };

        var booking = new Booking { Id = _testBookingId, UserId = Guid.NewGuid() }; // Different user

        _mockBookingRepository.Setup(r => r.GetByIdAsync(_testBookingId)).ReturnsAsync(booking);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CreateAsync(payment, _testUserId));
    }

    [Fact]
    public async Task CreateAsync_ThrowsException_WhenPaymentAlreadyExists()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = _testBookingId,
            Amount = 25m,
            Method = PaymentMethod.Card
        };

        var booking = new Booking { Id = _testBookingId, UserId = _testUserId };
        var existingPayment = new Payment { Id = Guid.NewGuid(), BookingId = _testBookingId };

        _mockBookingRepository.Setup(r => r.GetByIdAsync(_testBookingId)).ReturnsAsync(booking);
        _mockPaymentRepository.Setup(r => r.GetByBookingIdAsync(_testBookingId)).ReturnsAsync(existingPayment);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CreateAsync(payment, _testUserId));
    }

    [Fact]
    public async Task CreateAsync_ThrowsException_WhenValidationFails()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = Guid.Empty, // Invalid
            Amount = -10m, // Invalid
            Method = PaymentMethod.Card
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.CreateAsync(payment, _testUserId));
    }

    #endregion

    #region UpdateStatusAsync Tests

    [Fact]
    public async Task UpdateStatusAsync_UpdatesStatus_WhenValid()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            BookingId = _testBookingId,
            UserId = _testUserId,
            Status = PaymentStatus.Pending,
            Amount = 25m
        };

        var booking = new Booking { Id = _testBookingId, UserId = _testUserId, IsPaid = false };

        _mockPaymentRepository.Setup(r => r.GetByIdAsync(paymentId)).ReturnsAsync(payment);
        _mockBookingRepository.Setup(r => r.GetByIdAsync(_testBookingId)).ReturnsAsync(booking);
        _mockPaymentRepository.Setup(r => r.UpdateAsync(It.IsAny<Payment>()))
            .ReturnsAsync((Payment p) => p);
        _mockBookingRepository.Setup(r => r.UpdateAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking b) => b);

        // Act
        var result = await _handler.UpdateStatusAsync(paymentId, PaymentStatus.Completed);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be(PaymentStatus.Completed);
        _mockPaymentRepository.Verify(r => r.UpdateAsync(It.IsAny<Payment>()), Times.Once);
        _mockBookingRepository.Verify(r => r.UpdateAsync(It.Is<Booking>(b => b.IsPaid == true)), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_ReturnsNull_WhenPaymentNotFound()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        _mockPaymentRepository.Setup(r => r.GetByIdAsync(paymentId)).ReturnsAsync((Payment?)null);

        // Act
        var result = await _handler.UpdateStatusAsync(paymentId, PaymentStatus.Completed);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_ThrowsException_WhenCompletedPaymentChangedToNonRefunded()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            Status = PaymentStatus.Completed,
            Amount = 25m
        };

        _mockPaymentRepository.Setup(r => r.GetByIdAsync(paymentId)).ReturnsAsync(payment);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.UpdateStatusAsync(paymentId, PaymentStatus.Pending));
    }

    [Fact]
    public async Task UpdateStatusAsync_ThrowsException_WhenRefundedPaymentModified()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            Status = PaymentStatus.Refunded,
            Amount = 25m
        };

        _mockPaymentRepository.Setup(r => r.GetByIdAsync(paymentId)).ReturnsAsync(payment);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.UpdateStatusAsync(paymentId, PaymentStatus.Completed));
    }

    #endregion

    #region ProcessRefundAsync Tests

    [Fact]
    public async Task ProcessRefundAsync_ProcessesRefund_WhenValid()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            BookingId = _testBookingId,
            UserId = _testUserId,
            Status = PaymentStatus.Completed,
            Amount = 25m
        };

        var booking = new Booking { Id = _testBookingId, UserId = _testUserId, IsPaid = true };

        _mockPaymentRepository.Setup(r => r.GetByIdAsync(paymentId)).ReturnsAsync(payment);
        _mockBookingRepository.Setup(r => r.GetByIdAsync(_testBookingId)).ReturnsAsync(booking);
        _mockPaymentRepository.Setup(r => r.UpdateAsync(It.IsAny<Payment>()))
            .ReturnsAsync((Payment p) => p);
        _mockBookingRepository.Setup(r => r.UpdateAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking b) => b);

        // Act
        var result = await _handler.ProcessRefundAsync(paymentId, _testUserId);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be(PaymentStatus.Refunded);
        _mockBookingRepository.Verify(r => r.UpdateAsync(It.Is<Booking>(b => b.IsPaid == false)), Times.Once);
    }

    [Fact]
    public async Task ProcessRefundAsync_ReturnsNull_WhenPaymentNotFound()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        _mockPaymentRepository.Setup(r => r.GetByIdAsync(paymentId)).ReturnsAsync((Payment?)null);

        // Act
        var result = await _handler.ProcessRefundAsync(paymentId, _testUserId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ProcessRefundAsync_ThrowsException_WhenUserDoesNotOwnPayment()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            UserId = Guid.NewGuid(), // Different user
            Status = PaymentStatus.Completed,
            Amount = 25m
        };

        _mockPaymentRepository.Setup(r => r.GetByIdAsync(paymentId)).ReturnsAsync(payment);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.ProcessRefundAsync(paymentId, _testUserId));
    }

    [Fact]
    public async Task ProcessRefundAsync_ThrowsException_WhenPaymentNotCompleted()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            UserId = _testUserId,
            Status = PaymentStatus.Pending, // Not completed
            Amount = 25m
        };

        _mockPaymentRepository.Setup(r => r.GetByIdAsync(paymentId)).ReturnsAsync(payment);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _handler.ProcessRefundAsync(paymentId, _testUserId));
    }

    #endregion
}
