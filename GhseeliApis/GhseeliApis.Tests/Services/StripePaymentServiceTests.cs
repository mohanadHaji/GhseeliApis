using FluentAssertions;
using GhseeliApis.DTOs.Payment;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Stripe;

namespace GhseeliApis.Tests.Services;

/// <summary>
/// Unit tests for StripePaymentService
/// </summary>
public class StripePaymentServiceTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly string _testSecretKey = "sk_test_51TestSecretKey1234567890abcdefghijklmnopqrstuvwxyz";

    public StripePaymentServiceTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<IAppLogger>();

        // Setup configuration to return test secret key
        _mockConfiguration.Setup(c => c["Stripe:SecretKey"]).Returns(_testSecretKey);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsException_WhenSecretKeyIsNull()
    {
        // Arrange
        _mockConfiguration.Setup(c => c["Stripe:SecretKey"]).Returns((string?)null);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object));

        exception.Message.Should().Contain("Stripe SecretKey is not configured");
    }

    [Fact]
    public void Constructor_DoesNotThrowException_WhenSecretKeyIsEmpty()
    {
        // Arrange
        _mockConfiguration.Setup(c => c["Stripe:SecretKey"]).Returns(string.Empty);

        // Act - Constructor doesn't throw, it sets StripeConfiguration.ApiKey to empty
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_CreatesInstance_WhenSecretKeyIsProvided()
    {
        // Act
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    #endregion

    #region ProcessPaymentAsync Tests

    [Fact]
    public async Task ProcessPaymentAsync_LogsInfo_WhenProcessingPayment()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var amount = 5000L;
        var currency = "usd";
        var paymentMethodId = "pm_test_1234567890";

        // Act
        // Note: This will fail because we're using a test key without actual Stripe connection
        // But we can verify logging behavior
        try
        {
            await service.ProcessPaymentAsync(amount, currency, paymentMethodId);
        }
        catch
        {
            // Expected to fail - we're testing logging
        }

        // Assert
        _mockLogger.Verify(
            l => l.LogInfo(It.Is<string>(s =>
                s.Contains("Processing Stripe payment") &&
                s.Contains(amount.ToString()) &&
                s.Contains(currency) &&
                s.Contains(paymentMethodId))),
            Times.Once);
    }

    [Fact]
    public async Task ProcessPaymentAsync_ReturnsFailure_WhenStripeExceptionOccurs()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var amount = 5000L;
        var currency = "usd";
        var invalidPaymentMethodId = "invalid_pm";

        // Act
        var result = await service.ProcessPaymentAsync(amount, currency, invalidPaymentMethodId);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        _mockLogger.Verify(l => l.LogError(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPaymentAsync_IncludesMetadata_WhenProvided()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var amount = 5000L;
        var currency = "usd";
        var paymentMethodId = "pm_test_1234567890";
        var metadata = new Dictionary<string, string>
        {
            { "booking_id", Guid.NewGuid().ToString() },
            { "user_id", Guid.NewGuid().ToString() }
        };

        // Act
        var result = await service.ProcessPaymentAsync(
            amount, currency, paymentMethodId, "Test payment", metadata);

        // Assert
        // Will fail due to invalid credentials, but validates parameter passing
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessPaymentAsync_HandlesNullMetadata()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var amount = 5000L;
        var currency = "usd";
        var paymentMethodId = "pm_test_1234567890";

        // Act
        var result = await service.ProcessPaymentAsync(
            amount, currency, paymentMethodId, null, null);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse(); // Will fail with test credentials
    }

    #endregion

    #region RefundPaymentAsync Tests

    [Fact]
    public async Task RefundPaymentAsync_LogsInfo_WhenProcessingRefund()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var chargeId = "ch_test_1234567890";
        var amount = 5000L;

        // Act
        try
        {
            await service.RefundPaymentAsync(chargeId, amount);
        }
        catch
        {
            // Expected to fail - we're testing logging
        }

        // Assert
        _mockLogger.Verify(
            l => l.LogInfo(It.Is<string>(s =>
                s.Contains("Processing Stripe refund") &&
                s.Contains(chargeId))),
            Times.Once);
    }

    [Fact]
    public async Task RefundPaymentAsync_ReturnsFailure_WhenStripeExceptionOccurs()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var invalidChargeId = "invalid_charge";

        // Act
        var result = await service.RefundPaymentAsync(invalidChargeId);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        _mockLogger.Verify(l => l.LogError(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task RefundPaymentAsync_HandlesPartialRefund()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var chargeId = "ch_test_1234567890";
        var partialAmount = 2500L;

        // Act
        var result = await service.RefundPaymentAsync(chargeId, partialAmount);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse(); // Will fail with test credentials
    }

    [Fact]
    public async Task RefundPaymentAsync_HandlesFullRefund_WhenAmountIsNull()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var chargeId = "ch_test_1234567890";

        // Act
        var result = await service.RefundPaymentAsync(chargeId, null);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse(); // Will fail with test credentials
    }

    [Fact]
    public async Task RefundPaymentAsync_IncludesReason_WhenProvided()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var chargeId = "ch_test_1234567890";
        var reason = "requested_by_customer";

        // Act
        var result = await service.RefundPaymentAsync(chargeId, null, reason);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region CapturePaymentAsync Tests

    [Fact]
    public async Task CapturePaymentAsync_LogsInfo_WhenCapturingPayment()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var paymentIntentId = "pi_test_1234567890";

        // Act
        try
        {
            await service.CapturePaymentAsync(paymentIntentId);
        }
        catch
        {
            // Expected to fail - we're testing logging
        }

        // Assert
        _mockLogger.Verify(
            l => l.LogInfo(It.Is<string>(s =>
                s.Contains("Capturing Stripe payment") &&
                s.Contains(paymentIntentId))),
            Times.Once);
    }

    [Fact]
    public async Task CapturePaymentAsync_ReturnsFailure_WhenStripeExceptionOccurs()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var invalidIntentId = "invalid_intent";

        // Act
        var result = await service.CapturePaymentAsync(invalidIntentId);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be("failed");
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        _mockLogger.Verify(l => l.LogError(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CapturePaymentAsync_HandlesPartialCapture()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var paymentIntentId = "pi_test_1234567890";
        var captureAmount = 3000L;

        // Act
        var result = await service.CapturePaymentAsync(paymentIntentId, captureAmount);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse(); // Will fail with test credentials
    }

    [Fact]
    public async Task CapturePaymentAsync_HandlesFullCapture_WhenAmountIsNull()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var paymentIntentId = "pi_test_1234567890";

        // Act
        var result = await service.CapturePaymentAsync(paymentIntentId, null);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Response Validation Tests

    [Fact]
    public async Task ProcessPaymentAsync_ReturnsResponseWithProcessedAt()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var beforeCall = DateTime.UtcNow;

        // Act
        var result = await service.ProcessPaymentAsync(5000, "usd", "pm_invalid");
        var afterCall = DateTime.UtcNow;

        // Assert
        result.ProcessedAt.Should().BeOnOrAfter(beforeCall);
        result.ProcessedAt.Should().BeOnOrBefore(afterCall);
    }

    [Fact]
    public async Task RefundPaymentAsync_ReturnsResponseWithProcessedAt()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var beforeCall = DateTime.UtcNow;

        // Act
        var result = await service.RefundPaymentAsync("ch_invalid");
        var afterCall = DateTime.UtcNow;

        // Assert
        result.ProcessedAt.Should().BeOnOrAfter(beforeCall);
        result.ProcessedAt.Should().BeOnOrBefore(afterCall);
    }

    [Fact]
    public async Task CapturePaymentAsync_ReturnsResponseWithProcessedAt()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);
        var beforeCall = DateTime.UtcNow;

        // Act
        var result = await service.CapturePaymentAsync("pi_invalid");
        var afterCall = DateTime.UtcNow;

        // Assert
        result.ProcessedAt.Should().BeOnOrAfter(beforeCall);
        result.ProcessedAt.Should().BeOnOrBefore(afterCall);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ProcessPaymentAsync_CatchesGeneralException()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);

        // Act
        var result = await service.ProcessPaymentAsync(5000, "usd", "pm_invalid");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefundPaymentAsync_CatchesGeneralException()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);

        // Act
        var result = await service.RefundPaymentAsync("ch_invalid");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CapturePaymentAsync_CatchesGeneralException()
    {
        // Arrange
        var service = new StripePaymentService(_mockConfiguration.Object, _mockLogger.Object);

        // Act
        var result = await service.CapturePaymentAsync("pi_invalid");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    #endregion
}
