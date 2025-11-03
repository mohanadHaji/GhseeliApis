using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a specific service offering with pricing and duration
/// Can be company-specific or generic
/// </summary>
public class ServiceOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid ServiceId { get; set; }
    
    /// <summary>
    /// Optional: Company-specific pricing and options
    /// Null means this is a generic service option
    /// </summary>
    public Guid? CompanyId { get; set; }
    
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }

    /// <summary>
    /// Expected duration of service in minutes
    /// </summary>
    public int DurationMinutes { get; set; } = 30;

    [Precision(18, 2)]
    public decimal Price { get; set; } = 0m;

    // Navigation properties
    public Service Service { get; set; } = null!;
    public Company? Company { get; set; }
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
