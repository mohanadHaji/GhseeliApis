using GhseeliApis.Interfaces;
using Microsoft.AspNetCore.Identity;
using System.Text.RegularExpressions;

namespace GhseeliApis.Models;

/// <summary>
/// User entity extending IdentityUser with custom properties
/// </summary>
public class User : IdentityUser<int>, IValidatable
{
    // Custom properties (in addition to IdentityUser properties)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    public bool IsActive { get; set; } = true;

    // IdentityUser<int> already provides:
    // - Id (int)
    // - UserName (string)
    // - NormalizedUserName (string)
    // - Email (string)
    // - NormalizedEmail (string)
    // - EmailConfirmed (bool)
    // - PasswordHash (string)
    // - SecurityStamp (string)
    // - ConcurrencyStamp (string)
    // - PhoneNumber (string)
    // - PhoneNumberConfirmed (bool)
    // - TwoFactorEnabled (bool)
    // - LockoutEnd (DateTimeOffset?)
    // - LockoutEnabled (bool)
    // - AccessFailedCount (int)

    /// <summary>
    /// Validates the User model
    /// </summary>
    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };

        // Validate UserName (instead of Name)
        if (string.IsNullOrWhiteSpace(UserName))
        {
            result.AddError("Username is required.");
        }
        else if (UserName.Length < 2)
        {
            result.AddError("Username must be at least 2 characters long.");
        }
        else if (UserName.Length > 256)
        {
            result.AddError("Username cannot exceed 256 characters.");
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
        else if (Email.Length > 256)
        {
            result.AddError("Email cannot exceed 256 characters.");
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
