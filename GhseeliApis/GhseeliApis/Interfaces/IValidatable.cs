namespace GhseeliApis.Interfaces;

/// <summary>
/// Interface for models that support validation
/// </summary>
public interface IValidatable
{
    /// <summary>
    /// Validates the model and returns validation result
    /// </summary>
    /// <returns>Validation result with success status and error messages</returns>
    ValidationResult Validate();
}

/// <summary>
/// Validation result containing success status and error messages
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();

    public static ValidationResult Success() => new() { IsValid = true };

    public static ValidationResult Failure(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors.ToList()
    };

    public void AddError(string error)
    {
        IsValid = false;
        Errors.Add(error);
    }
}
