using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a car washing service company
/// </summary>
public class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(30)]
    public string? Phone { get; set; }
    
    public string? Description { get; set; }
    
    /// <summary>
    /// Textual description of service area (could be enhanced with geo-polygons later)
    /// </summary>
    [MaxLength(200)]
    public string? ServiceAreaDescription { get; set; }

    // Navigation properties
    public ICollection<CompanyAvailability> Availabilities { get; set; } = new List<CompanyAvailability>();
    public ICollection<ServiceOption> ServiceOptions { get; set; } = new List<ServiceOption>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
