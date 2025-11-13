using System.ComponentModel.DataAnnotations;
using GhseeliApis.Interfaces;
using ValidationResult = GhseeliApis.Interfaces.ValidationResult;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a car washing service company
/// </summary>
public class Company : IValidatable
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

    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(Name))
        {
            result.AddError("Company name is required.");
        }
        else if (Name.Length < 2)
        {
            result.AddError("Company name must be at least 2 characters long.");
        }
        else if (Name.Length > 200)
        {
            result.AddError("Company name cannot exceed 200 characters.");
        }

        if (!string.IsNullOrWhiteSpace(Phone) && Phone.Length > 30)
        {
            result.AddError("Phone number cannot exceed 30 characters.");
        }

        if (!string.IsNullOrWhiteSpace(ServiceAreaDescription) && ServiceAreaDescription.Length > 200)
        {
            result.AddError("Service area description cannot exceed 200 characters.");
        }

        return result;
    }
}
