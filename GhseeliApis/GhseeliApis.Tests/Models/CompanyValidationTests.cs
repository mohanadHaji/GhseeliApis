using FluentAssertions;
using GhseeliApis.Models;

namespace GhseeliApis.Tests.Models;

/// <summary>
/// Unit tests for Company model validation
/// </summary>
public class CompanyValidationTests
{
    [Fact]
    public void Validate_ReturnsValid_WhenAllRequiredFieldsAreProvided()
    {
        // Arrange
        var company = new Company
        {
            Name = "ABC Car Wash",
            Phone = "555-1234"
        };

        // Act
        var result = company.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenNameIsEmpty()
    {
        // Arrange
        var company = new Company
        {
            Name = "",
            Phone = "555-1234"
        };

        // Act
        var result = company.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Name") || e.Contains("name"));
    }

    [Fact]
    public void Validate_ReturnsValid_WithOptionalFields()
    {
        // Arrange
        var company = new Company
        {
            Name = "ABC Car Wash",
            Phone = "555-1234",
            Description = "Professional car washing service",
            ServiceAreaDescription = "Downtown area"
        };

        // Act
        var result = company.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsValid_WhenPhoneIsMissing()
    {
        // Arrange
        var company = new Company
        {
            Name = "ABC Car Wash"
        };

        // Act
        var result = company.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsInvalid_WhenNameIsTooShort()
    {
        // Arrange
        var company = new Company
        {
            Name = "A"  // Less than 2 characters
        };

        // Act
        var result = company.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least") || e.Contains("2 characters"));
    }
}
