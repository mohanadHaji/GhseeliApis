using FluentAssertions;
using GhseeliApis.Models;

namespace GhseeliApis.Tests.Models;

/// <summary>
/// Unit tests for UserAddress model validation
/// </summary>
public class UserAddressValidationTests
{
    [Fact]
    public void Validate_ReturnsValid_WhenAllRequiredFieldsAreProvided()
    {
        // Arrange
        var address = new UserAddress
        {
            UserId = Guid.NewGuid(),
            AddressLine = "123 Main Street"
        };

        // Act
        var result = address.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenAddressLineIsEmpty()
    {
        // Arrange
        var address = new UserAddress
        {
            UserId = Guid.NewGuid(),
            AddressLine = ""
        };

        // Act
        var result = address.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Address line") || e.Contains("AddressLine"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenAddressLineIsTooShort()
    {
        // Arrange
        var address = new UserAddress
        {
            UserId = Guid.NewGuid(),
            AddressLine = "123"  // Less than 5 characters
        };

        // Act
        var result = address.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least") || e.Contains("5 characters"));
    }

    [Fact]
    public void Validate_ReturnsValid_WithOptionalCity()
    {
        // Arrange
        var address = new UserAddress
        {
            UserId = Guid.NewGuid(),
            AddressLine = "123 Main Street",
            City = "Test City"
        };

        // Act
        var result = address.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsValid_WithOptionalArea()
    {
        // Arrange
        var address = new UserAddress
        {
            UserId = Guid.NewGuid(),
            AddressLine = "123 Main Street",
            Area = "Downtown"
        };

        // Act
        var result = address.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsValid_WithAllOptionalFields()
    {
        // Arrange
        var address = new UserAddress
        {
            UserId = Guid.NewGuid(),
            AddressLine = "123 Main Street",
            City = "Test City",
            Area = "Downtown",
            Latitude = 25.2048,
            Longitude = 55.2708,
            IsPrimary = true
        };

        // Act
        var result = address.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenLatitudeIsOutOfRange()
    {
        // Arrange
        var address = new UserAddress
        {
            UserId = Guid.NewGuid(),
            AddressLine = "123 Main Street",
            Latitude = 100  // Invalid: > 90
        };

        // Act
        var result = address.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Latitude"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenLongitudeIsOutOfRange()
    {
        // Arrange
        var address = new UserAddress
        {
            UserId = Guid.NewGuid(),
            AddressLine = "123 Main Street",
            Longitude = 200  // Invalid: > 180
        };

        // Act
        var result = address.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Longitude"));
    }
}
