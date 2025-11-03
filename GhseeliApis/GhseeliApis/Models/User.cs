using System.ComponentModel.DataAnnotations;
using GhseeliApis.Interfaces;
using Microsoft.AspNetCore.Identity;
using ValidationResult = GhseeliApis.Interfaces.ValidationResult;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a user (car owner) in the system
/// </summary>
public class User : IdentityUser<Guid>, IValidatable
{
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;
    
    [MaxLength(30)]
    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Wallet? Wallet { get; set; }
    public ICollection<UserAddress> Addresses { get; set; } = new List<UserAddress>();
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<WalletTransaction> WalletTransactions { get; set; } = new List<WalletTransaction>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Validates the User model
    /// </summary>
    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };

        // Validate FullName
        if (string.IsNullOrWhiteSpace(FullName))
        {
            result.AddError("Full name is required.");
        }
        else if (FullName.Length < 2)
        {
            result.AddError("Full name must be at least 2 characters long.");
        }
        else if (FullName.Length > 150)
        {
            result.AddError("Full name cannot exceed 150 characters.");
        }

        // Validate Email (from IdentityUser)
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

        // Validate UserName (from IdentityUser)
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

        // Validate Phone (optional but must be valid if provided)
        if (!string.IsNullOrWhiteSpace(Phone) && Phone.Length > 30)
        {
            result.AddError("Phone number cannot exceed 30 characters.");
        }

        return result;
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var emailRegex = new System.Text.RegularExpressions.Regex(
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled,
                TimeSpan.FromMilliseconds(250));

            return emailRegex.IsMatch(email);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
