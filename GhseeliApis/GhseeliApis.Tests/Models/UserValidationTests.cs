using FluentAssertions;
using GhseeliApis.Models;

namespace GhseeliApis.Tests.Models;

/// <summary>
/// Unit tests for User model validation
/// </summary>
public class UserValidationTests
{
    #region Name Validation Tests

    [Fact]
    public void Validate_ReturnsError_WhenNameIsEmpty()
    {
        // Arrange
        var user = new User
        {
            Name = "",
            Email = "test@example.com"
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Name is required.");
    }

    [Fact]
    public void Validate_ReturnsError_WhenNameIsWhitespace()
    {
        // Arrange
        var user = new User
        {
            Name = "   ",
            Email = "test@example.com"
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Name is required.");
    }

    [Fact]
    public void Validate_ReturnsError_WhenNameIsTooShort()
    {
        // Arrange
        var user = new User
        {
            Name = "A",
            Email = "test@example.com"
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Name must be at least 2 characters long.");
    }

    [Fact]
    public void Validate_ReturnsError_WhenNameIsTooLong()
    {
        // Arrange
        var user = new User
        {
            Name = new string('A', 101), // 101 characters
            Email = "test@example.com"
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Name cannot exceed 100 characters.");
    }

    [Fact]
    public void Validate_Succeeds_WhenNameIsValid()
    {
        // Arrange
        var user = new User
        {
            Name = "John Doe",
            Email = "john@example.com"
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region Email Validation Tests

    [Fact]
    public void Validate_ReturnsError_WhenEmailIsEmpty()
    {
        // Arrange
        var user = new User
        {
            Name = "John Doe",
            Email = ""
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Email is required.");
    }

    [Fact]
    public void Validate_ReturnsError_WhenEmailIsWhitespace()
    {
        // Arrange
        var user = new User
        {
            Name = "John Doe",
            Email = "   "
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Email is required.");
    }

    [Fact]
    public void Validate_ReturnsError_WhenEmailFormatIsInvalid()
    {
        // Arrange
        var invalidEmails = new[]
        {
            "notanemail",
            "@example.com",
            "user@",
            "user@domain",
            "user domain@example.com",
            "user@domain .com"
        };

        foreach (var email in invalidEmails)
        {
            var user = new User
            {
                Name = "John Doe",
                Email = email
            };

            // Act
            var result = user.Validate();

            // Assert
            result.IsValid.Should().BeFalse($"Email '{email}' should be invalid");
            result.Errors.Should().Contain("Email format is invalid.", $"Email '{email}' should have format error");
        }
    }

    [Fact]
    public void Validate_ReturnsError_WhenEmailIsTooLong()
    {
        // Arrange
        var user = new User
        {
            Name = "John Doe",
            Email = new string('a', 190) + "@example.com" // 201 characters total
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Email cannot exceed 200 characters.");
    }

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("john.doe@company.co.uk")]
    [InlineData("test123@test-domain.com")]
    [InlineData("name+tag@domain.org")]
    public void Validate_Succeeds_WhenEmailIsValid(string email)
    {
        // Arrange
        var user = new User
        {
            Name = "John Doe",
            Email = email
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region Multiple Errors Tests

    [Fact]
    public void Validate_ReturnsMultipleErrors_WhenMultipleFieldsAreInvalid()
    {
        // Arrange
        var user = new User
        {
            Name = "", // Invalid - empty
            Email = "notanemail" // Invalid - bad format
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain("Name is required.");
        result.Errors.Should().Contain("Email format is invalid.");
    }

    [Fact]
    public void Validate_ReturnsAllErrors_WhenAllFieldsAreInvalid()
    {
        // Arrange
        var user = new User
        {
            Name = "A", // Too short
            Email = "invalid" // Bad format
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Validate_Succeeds_WhenMinimumValidData()
    {
        // Arrange
        var user = new User
        {
            Name = "AB", // Minimum 2 characters
            Email = "a@b.c" // Minimum valid email
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Succeeds_WhenMaximumValidData()
    {
        // Arrange
        var user = new User
        {
            Name = new string('A', 100), // Maximum 100 characters
            Email = new string('a', 188) + "@example.com" // Maximum 200 characters
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion
}
