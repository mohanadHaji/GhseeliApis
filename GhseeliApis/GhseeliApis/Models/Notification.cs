using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a notification sent to a user
/// </summary>
public class Notification
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
}
