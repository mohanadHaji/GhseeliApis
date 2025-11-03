using FluentAssertions;
using GhseeliApis.Validators;

namespace GhseeliApis.Tests.Validators;

/// <summary>
/// Unit tests for IdValidator
/// </summary>
public class IdValidatorTests
{
    #region ValidateId Tests

    [Fact]
    public void ValidateId_ReturnsSuccess_WhenIdIsPositive()
    {
        // Arrange
        int validId = 1;

        // Act
        var result = IdValidator.ValidateId(validId);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateId_ReturnsSuccess_WhenIdIsLargePositiveNumber()
    {
        // Arrange
        int validId = 999999;

        // Act
        var result = IdValidator.ValidateId(validId);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateId_ReturnsError_WhenIdIsZero()
    {
        // Arrange
        int invalidId = 0;

        // Act
        var result = IdValidator.ValidateId(invalidId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors.Should().Contain("User ID must be greater than zero.");
    }

    [Fact]
    public void ValidateId_ReturnsError_WhenIdIsNegative()
    {
        // Arrange
        int invalidId = -1;

        // Act
        var result = IdValidator.ValidateId(invalidId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors.Should().Contain("User ID must be greater than zero.");
    }

    [Fact]
    public void ValidateId_ReturnsError_WhenIdIsLargeNegativeNumber()
    {
        // Arrange
        int invalidId = -999999;

        // Act
        var result = IdValidator.ValidateId(invalidId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors.Should().Contain("User ID must be greater than zero.");
    }

    #endregion

    #region ValidateId with EntityName Tests

    [Fact]
    public void ValidateIdWithEntityName_ReturnsSuccess_WhenIdIsPositive()
    {
        // Arrange
        int validId = 1;
        string entityName = "Product";

        // Act
        var result = IdValidator.ValidateId(validId, entityName);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateIdWithEntityName_ReturnsCustomError_WhenIdIsZero()
    {
        // Arrange
        int invalidId = 0;
        string entityName = "Product";

        // Act
        var result = IdValidator.ValidateId(invalidId, entityName);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors.Should().Contain("Product ID must be greater than zero.");
    }

    [Fact]
    public void ValidateIdWithEntityName_ReturnsCustomError_WhenIdIsNegative()
    {
        // Arrange
        int invalidId = -1;
        string entityName = "Order";

        // Act
        var result = IdValidator.ValidateId(invalidId, entityName);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors.Should().Contain("Order ID must be greater than zero.");
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Product")]
    [InlineData("Order")]
    [InlineData("Customer")]
    public void ValidateIdWithEntityName_UsesCorrectEntityNameInError(string entityName)
    {
        // Arrange
        int invalidId = 0;

        // Act
        var result = IdValidator.ValidateId(invalidId, entityName);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain($"{entityName} ID must be greater than zero.");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ValidateId_ReturnsSuccess_ForMinimumValidId()
    {
        // Arrange
        int validId = 1; // Minimum valid ID

        // Act
        var result = IdValidator.ValidateId(validId);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateId_ReturnsSuccess_ForMaxInt()
    {
        // Arrange
        int validId = int.MaxValue;

        // Act
        var result = IdValidator.ValidateId(validId);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateId_ReturnsError_ForMinInt()
    {
        // Arrange
        int invalidId = int.MinValue;

        // Act
        var result = IdValidator.ValidateId(invalidId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
    }

    #endregion
}
