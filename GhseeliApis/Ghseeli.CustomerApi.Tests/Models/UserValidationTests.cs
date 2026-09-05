using FluentAssertions;
using GhseeliApis.Models;

namespace GhseeliApis.Tests.Models;

/// <summary>
/// Unit tests for User model validation
/// </summary>
public class UserValidationTests
{
    #region UserName Validation Tests

    [Fact]
    public void Validate_ReturnsError_WhenUserNameIsEmpty()
    {
        // Arrange
        var user = new User
        {
            UserName = "",
            Email = "test@example.com",
            FullName = "Test User"
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Username is required.");
    }

    [Fact]
    public void Validate_ReturnsError_WhenUserNameIsWhitespace()
    {
        // Arrange
        var user = new User
        {
            UserName = "   ",
            Email = "test@example.com",
            FullName = "Test User"
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Username is required.");
    }

    [Fact]
    public void Validate_ReturnsError_WhenUserNameIsTooShort()
    {
        // Arrange
        var user = new User
        {
            UserName = "A",
            Email = "test@example.com",
            FullName = "Test User"
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Username must be at least 2 characters long.");
    }

    [Fact]
    public void Validate_ReturnsError_WhenUserNameIsTooLong()
    {
        // Arrange
        var user = new User
        {
            UserName = new string('A', 257), // 257 characters (max is 256)
            Email = "test@example.com",
            FullName = "Test User"
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Username cannot exceed 256 characters.");
    }

    [Fact]
    public void Validate_Succeeds_WhenUserNameIsValid()
    {
        // Arrange
        var user = new User
        {
            UserName = "JohnDoe",
            Email = "john@example.com",
            FullName = "John Doe"
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
            UserName = "JohnDoe",
            Email = "",
            FullName = "John Doe"
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
            UserName = "JohnDoe",
            Email = "   ",
            FullName = "John Doe"
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
                UserName = "JohnDoe",
                Email = email,
                FullName = "John Doe"
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
            UserName = "JohnDoe",
            Email = new string('a', 246) + "@example.com", // 257 characters total (max is 256)
            FullName = "John Doe"
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Email cannot exceed 256 characters.");
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
            UserName = "JohnDoe",
            Email = email,
            FullName = "John Doe"
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
            UserName = "", // Invalid - empty
            Email = "notanemail", // Invalid - bad format
            FullName = "" // Invalid - empty
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
        result.Errors.Should().Contain("Full name is required.");
        result.Errors.Should().Contain("Email format is invalid.");
        result.Errors.Should().Contain("Username is required.");
    }

    [Fact]
    public void Validate_ReturnsAllErrors_WhenAllFieldsAreInvalid()
    {
        // Arrange
        var user = new User
        {
            UserName = "A", // Too short
            Email = "invalid", // Bad format
            FullName = "A" // Too short
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Validate_Succeeds_WhenMinimumValidData()
    {
        // Arrange
        var user = new User
        {
            UserName = "AB", // Minimum 2 characters
            Email = "a@b.c", // Minimum valid email
            FullName = "AB" // Minimum 2 characters
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
            UserName = new string('A', 256), // Maximum 256 characters
            Email = new string('a', 244) + "@example.com", // Maximum 256 characters
            FullName = new string('A', 150) // Maximum 150 characters
        };

        // Act
        var result = user.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion
}
