using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;

namespace GhseeliApis.Tests.Models;

/// <summary>
/// Unit tests for Booking model validation
/// </summary>
public class BookingValidationTests
{
    [Fact]
    public void Validate_ReturnsValid_WhenAllFieldsAreValid()
    {
        // Arrange
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            ServiceOptionId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1),
            EndDateTime = DateTime.UtcNow.AddDays(1).AddHours(1),
            Status = BookingStatus.Pending
        };

        // Act
        var result = booking.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenUserIdIsEmpty()
    {
        // Arrange
        var booking = new Booking
        {
            UserId = Guid.Empty,
            CompanyId = Guid.NewGuid(),
            ServiceOptionId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1),
            EndDateTime = DateTime.UtcNow.AddDays(1).AddHours(1)
        };

        // Act
        var result = booking.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("User ID"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenCompanyIdIsEmpty()
    {
        // Arrange
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            CompanyId = Guid.Empty,
            ServiceOptionId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1),
            EndDateTime = DateTime.UtcNow.AddDays(1).AddHours(1)
        };

        // Act
        var result = booking.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Company ID"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenServiceOptionIdIsEmpty()
    {
        // Arrange
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            ServiceOptionId = Guid.Empty,
            VehicleId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1),
            EndDateTime = DateTime.UtcNow.AddDays(1).AddHours(1)
        };

        // Act
        var result = booking.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Service option"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenVehicleIdIsEmpty()
    {
        // Arrange
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            ServiceOptionId = Guid.NewGuid(),
            VehicleId = Guid.Empty,
            AddressId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1),
            EndDateTime = DateTime.UtcNow.AddDays(1).AddHours(1)
        };

        // Act
        var result = booking.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Vehicle ID"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenAddressIdIsEmpty()
    {
        // Arrange
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            ServiceOptionId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            AddressId = Guid.Empty,
            StartDateTime = DateTime.UtcNow.AddDays(1),
            EndDateTime = DateTime.UtcNow.AddDays(1).AddHours(1)
        };

        // Act
        var result = booking.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Address ID"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenEndDateTimeBeforeStartDateTime()
    {
        // Arrange
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            ServiceOptionId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1),
            EndDateTime = DateTime.UtcNow  // Before start
        };

        // Act
        var result = booking.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("End date") || e.Contains("after start"));
    }

    [Fact]
    public void Validate_ReturnsMultipleErrors_WhenMultipleFieldsInvalid()
    {
        // Arrange
        var booking = new Booking
        {
            UserId = Guid.Empty,
            CompanyId = Guid.Empty,
            ServiceOptionId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(),
            StartDateTime = DateTime.UtcNow.AddDays(1),
            EndDateTime = DateTime.UtcNow
        };

        // Act
        var result = booking.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
    }
}
