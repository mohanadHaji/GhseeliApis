using GhseeliApis.Interfaces;

namespace GhseeliApis.Validators;

/// <summary>
/// Validator for ID parameters used in API endpoints
/// </summary>
public static class IdValidator
{
    /// <summary>
    /// Validates that an ID is greater than zero
    /// </summary>
    /// <param name="id">The ID to validate</param>
    /// <returns>Validation result</returns>
    public static ValidationResult ValidateId(int id)
    {
        var result = new ValidationResult { IsValid = true };

        if (id <= 0)
        {
            result.AddError("User ID must be greater than zero.");
        }

        return result;
    }

    /// <summary>
    /// Validates that an ID is greater than zero with custom error message
    /// </summary>
    /// <param name="id">The ID to validate</param>
    /// <param name="entityName">The name of the entity (e.g., "User", "Product")</param>
    /// <returns>Validation result</returns>
    public static ValidationResult ValidateId(int id, string entityName)
    {
        var result = new ValidationResult { IsValid = true };

        if (id <= 0)
        {
            result.AddError($"{entityName} ID must be greater than zero.");
        }

        return result;
    }
}
