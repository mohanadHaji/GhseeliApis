using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a user's saved address for car washing services
/// </summary>
public class UserAddress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid UserId { get; set; }
    
    [Required]
    [MaxLength(300)]
    public string AddressLine { get; set; } = string.Empty;
    
    [MaxLength(120)]
    public string? City { get; set; }
    
    [MaxLength(120)]
    public string? Area { get; set; }
    
    public double? Latitude { get; set; }
    
    public double? Longitude { get; set; }
    
    public bool IsPrimary { get; set; } = false;

    // Navigation properties
    public User User { get; set; } = null!;
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
