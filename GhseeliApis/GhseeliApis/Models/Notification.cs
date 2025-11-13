using System.ComponentModel.DataAnnotations;
using GhseeliApis.Interfaces;
using ValidationResult = GhseeliApis.Interfaces.ValidationResult;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a notification sent to a user
/// </summary>
public class Notification : IValidatable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid UserId { get; set; }
    
    [Required]
    public string Title { get; set; } = string.Empty;
    
    public string? Message { get; set; }
    
    public bool IsRead { get; set; } = false;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;

    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(Title))
        {
            result.AddError("Notification title is required.");
        }
        else if (Title.Length > 200)
        {
            result.AddError("Notification title cannot exceed 200 characters.");
        }

        if (!string.IsNullOrWhiteSpace(Message) && Message.Length > 1000)
        {
            result.AddError("Notification message cannot exceed 1000 characters.");
        }

        if (UserId == Guid.Empty)
        {
            result.AddError("User ID is required.");
        }

        return result;
    }
}
