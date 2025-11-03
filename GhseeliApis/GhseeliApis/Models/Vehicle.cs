using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a vehicle owned by a user
/// </summary>
public class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid UserId { get; set; }
    
    [MaxLength(150)]
    public string? Make { get; set; }
    
    [MaxLength(150)]
    public string? Model { get; set; }
    
    [MaxLength(50)]
    public string? Year { get; set; }
    
    [MaxLength(50)]
    public string? LicensePlate { get; set; }
    
    public string? Color { get; set; }

    // Navigation properties
    public User Owner { get; set; } = null!;
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
