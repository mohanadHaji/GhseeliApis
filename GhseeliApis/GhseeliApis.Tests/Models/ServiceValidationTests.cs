using FluentAssertions;
using GhseeliApis.Models;

namespace GhseeliApis.Tests.Models;

/// <summary>
/// Unit tests for Service model validation
/// </summary>
public class ServiceValidationTests
{
    [Fact]
    public void Validate_ReturnsValid_WhenNameIsProvided()
    {
        // Arrange
        var service = new Service
        {
            Name = "Basic Wash"
        };

        // Act
        var result = service.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenNameIsEmpty()
    {
        // Arrange
        var service = new Service
        {
            Name = ""
        };

        // Act
        var result = service.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("name") || e.Contains("required"));
    }

    [Fact]
    public void Validate_ReturnsValid_WithDescription()
    {
        // Arrange
        var service = new Service
        {
            Name = "Basic Wash",
            Description = "Standard car wash service"
        };

        // Act
        var result = service.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsValid_WithEmptyDescription()
    {
        // Arrange
        var service = new Service
        {
            Name = "Basic Wash",
            Description = ""
        };

        // Act
        var result = service.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenNameIsTooShort()
    {
        // Arrange
        var service = new Service
        {
            Name = "A"  // Less than 2 characters
        };

        // Act
        var result = service.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least") || e.Contains("2 characters"));
    }
}
