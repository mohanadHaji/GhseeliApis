using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;

namespace GhseeliApis.Tests.Models;

/// <summary>
/// Unit tests for Payment model validation
/// </summary>
public class PaymentValidationTests
{
    [Fact]
    public void Validate_ReturnsValid_WhenAllFieldsAreValid()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Amount = 25.50m,
            Method = PaymentMethod.Card,
            Status = PaymentStatus.Completed
        };

        // Act
        var result = payment.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenBookingIdIsEmpty()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = Guid.Empty,
            UserId = Guid.NewGuid(),
            Amount = 25.50m,
            Method = PaymentMethod.Card,
            Status = PaymentStatus.Completed
        };

        // Act
        var result = payment.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Booking"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenUserIdIsEmpty()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = Guid.NewGuid(),
            UserId = Guid.Empty,
            Amount = 25.50m,
            Method = PaymentMethod.Card,
            Status = PaymentStatus.Completed
        };

        // Act
        var result = payment.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("User"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenAmountIsNegative()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Amount = -10m,
            Method = PaymentMethod.Card,
            Status = PaymentStatus.Completed
        };

        // Act
        var result = payment.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Amount") || e.Contains("amount"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenAmountIsZero()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Amount = 0m,
            Method = PaymentMethod.Card,
            Status = PaymentStatus.Completed
        };

        // Act
        var result = payment.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Amount") || e.Contains("amount"));
    }

    [Fact]
    public void Validate_ReturnsMultipleErrors_WhenMultipleFieldsInvalid()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = Guid.Empty,
            UserId = Guid.Empty,
            Amount = -5m
        };

        // Act
        var result = payment.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Validate_ReturnsValid_WithWalletPaymentMethod()
    {
        // Arrange
        var payment = new Payment
        {
            BookingId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Amount = 50m,
            Method = PaymentMethod.Wallet,
            Status = PaymentStatus.Completed
        };

        // Act
        var result = payment.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
