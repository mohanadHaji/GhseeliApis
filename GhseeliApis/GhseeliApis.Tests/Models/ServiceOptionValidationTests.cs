using FluentAssertions;
using GhseeliApis.Models;

namespace GhseeliApis.Tests.Models;

/// <summary>
/// Unit tests for ServiceOption model validation
/// </summary>
public class ServiceOptionValidationTests
{
    [Fact]
    public void Validate_ReturnsValid_WhenAllFieldsAreValid()
    {
        // Arrange
        var serviceOption = new ServiceOption
        {
            ServiceId = Guid.NewGuid(),
            Name = "Standard Package",
            Price = 25.50m,
            DurationMinutes = 30
        };

        // Act
        var result = serviceOption.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenNameIsEmpty()
    {
        // Arrange
        var serviceOption = new ServiceOption
        {
            ServiceId = Guid.NewGuid(),
            Name = "",
            Price = 25.50m,
            DurationMinutes = 30
        };

        // Act
        var result = serviceOption.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Name") || e.Contains("name"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenNameIsTooShort()
    {
        // Arrange
        var serviceOption = new ServiceOption
        {
            ServiceId = Guid.NewGuid(),
            Name = "A",  // Less than 2 characters
            Price = 25.50m,
            DurationMinutes = 30
        };

        // Act
        var result = serviceOption.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least") || e.Contains("2 characters"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenPriceIsNegative()
    {
        // Arrange
        var serviceOption = new ServiceOption
        {
            ServiceId = Guid.NewGuid(),
            Name = "Standard Package",
            Price = -10m,
            DurationMinutes = 30
        };

        // Act
        var result = serviceOption.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Price") || e.Contains("price"));
    }

    [Fact]
    public void Validate_ReturnsValid_WhenPriceIsZero()
    {
        // Arrange - Price can be 0 (free service)
        var serviceOption = new ServiceOption
        {
            ServiceId = Guid.NewGuid(),
            Name = "Standard Package",
            Price = 0m,
            DurationMinutes = 30
        };

        // Act
        var result = serviceOption.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenDurationIsZero()
    {
        // Arrange
        var serviceOption = new ServiceOption
        {
            ServiceId = Guid.NewGuid(),
            Name = "Standard Package",
            Price = 25.50m,
            DurationMinutes = 0
        };

        // Act
        var result = serviceOption.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Duration"));
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenDurationIsNegative()
    {
        // Arrange
        var serviceOption = new ServiceOption
        {
            ServiceId = Guid.NewGuid(),
            Name = "Standard Package",
            Price = 25.50m,
            DurationMinutes = -15
        };

        // Act
        var result = serviceOption.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Duration"));
    }

    [Fact]
    public void Validate_ReturnsValid_WithOptionalCompanyId()
    {
        // Arrange
        var serviceOption = new ServiceOption
        {
            ServiceId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            Name = "Standard Package",
            Price = 25.50m,
            DurationMinutes = 30,
            Description = "Standard wash package"
        };

        // Act
        var result = serviceOption.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsMultipleErrors_WhenMultipleFieldsInvalid()
    {
        // Arrange
        var serviceOption = new ServiceOption
        {
            ServiceId = Guid.NewGuid(),
            Name = "",
            Price = -10m,
            DurationMinutes = 0
        };

        // Act
        var result = serviceOption.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
    }
}
