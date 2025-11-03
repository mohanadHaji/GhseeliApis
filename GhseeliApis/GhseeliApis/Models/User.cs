using GhseeliApis.Interfaces;
using System.Text.RegularExpressions;

namespace GhseeliApis.Models;

/// <summary>
/// Sample User entity - demonstrates how to create models for Google Cloud SQL
/// </summary>
public class User : IValidatable
{
    public int Id { get; set; }
    
    public string Name { get; set; } = string.Empty;
    
    public string Email { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Validates the User model
    /// </summary>
    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };

        // Validate Name
        if (string.IsNullOrWhiteSpace(Name))
        {
            result.AddError("Name is required.");
        }
        else if (Name.Length < 2)
        {
            result.AddError("Name must be at least 2 characters long.");
        }
        else if (Name.Length > 100)
        {
            result.AddError("Name cannot exceed 100 characters.");
        }

        // Validate Email
        if (string.IsNullOrWhiteSpace(Email))
        {
            result.AddError("Email is required.");
        }
        else if (!IsValidEmail(Email))
        {
            result.AddError("Email format is invalid.");
        }
        else if (Email.Length > 200)
        {
            result.AddError("Email cannot exceed 200 characters.");
        }

        return result;
    }

    /// <summary>
    /// Validates email format using regex
    /// </summary>
    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            // Simple email validation regex
            var emailRegex = new Regex(
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled,
                TimeSpan.FromMilliseconds(250));

            return emailRegex.IsMatch(email);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
