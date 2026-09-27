using FluentAssertions;
using Ghseeli.IntegrationContracts.Vehicles;
using GhseeliApis.Models;

namespace GhseeliApis.Tests.Models;

/// <summary>
/// Unit tests for Vehicle model validation
/// </summary>
public class VehicleValidationTests
{
    [Fact]
    public void Validate_ReturnsValid_WhenAllFieldsAreProvided()
    {
        // Arrange
        var vehicle = new Vehicle
        {
            VehicleType = VehicleType.Sedan,
            ImageUrl = "https://cdn.example.test/vehicles/sedan.png",
            Make = "Toyota",
            Model = "Camry",
            Year = "2023",
            UserId = Guid.NewGuid()
        };

        // Act
        var result = vehicle.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://cdn.example.test/vehicle.png")]
    [InlineData("/images/vehicle.png")]
    [InlineData("https://user:password@cdn.example.test/vehicle.png")]
    [InlineData("not-a-url")]
    public void Validate_ReturnsInvalid_WhenImageUrlIsNotSafeAbsoluteHttps(string imageUrl)
    {
        var vehicle = new Vehicle
        {
            VehicleType = VehicleType.Sedan,
            ImageUrl = imageUrl
        };

        var result = vehicle.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("image URL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenImageUrlExceedsMaximumLength()
    {
        var vehicle = new Vehicle
        {
            VehicleType = VehicleType.Sedan,
            ImageUrl = $"https://cdn.example.test/{new string('a', 480)}.png"
        };

        vehicle.ImageUrl!.Length.Should().BeGreaterThan(500);
        vehicle.Validate().IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ReturnsValid_WhenMakeIsEmpty()
    {
        // Arrange - Make is optional
        var vehicle = new Vehicle
        {
            Make = "",
            Model = "Camry",
            Year = "2023",
            UserId = Guid.NewGuid()
        };

        // Act
        var result = vehicle.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsValid_WhenModelIsEmpty()
    {
        // Arrange - Model is optional
        var vehicle = new Vehicle
        {
            Make = "Toyota",
            Model = "",
            Year = "2023",
            UserId = Guid.NewGuid()
        };

        // Act
        var result = vehicle.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsValid_WhenYearIsEmpty()
    {
        // Arrange - Year is optional
        var vehicle = new Vehicle
        {
            Make = "Toyota",
            Model = "Camry",
            Year = "",
            UserId = Guid.NewGuid()
        };

        // Act
        var result = vehicle.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenYearIsOutOfRange()
    {
        // Arrange
        var vehicle = new Vehicle
        {
            Make = "Toyota",
            Model = "Camry",
            Year = "1800",  // Before 1900
            UserId = Guid.NewGuid()
        };

        // Act
        var result = vehicle.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Year") || e.Contains("1900"));
    }

    [Fact]
    public void Validate_ReturnsValid_WithOptionalFieldsProvided()
    {
        // Arrange
        var vehicle = new Vehicle
        {
            Make = "Toyota",
            Model = "Camry",
            Year = "2023",
            Color = "Black",
            LicensePlate = "ABC123",
            UserId = Guid.NewGuid()
        };

        // Act
        var result = vehicle.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenMakeTooLong()
    {
        // Arrange
        var vehicle = new Vehicle
        {
            Make = new string('A', 151),  // 151 characters, exceeds 150 limit
            Model = "Camry",
            Year = "2023",
            UserId = Guid.NewGuid()
        };

        // Act
        var result = vehicle.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Make"));
    }
}
