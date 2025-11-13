using System.ComponentModel.DataAnnotations;
using GhseeliApis.Interfaces;
using ValidationResult = GhseeliApis.Interfaces.ValidationResult;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a user's saved address for car washing services
/// </summary>
public class UserAddress : IValidatable
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

    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(AddressLine))
        {
            result.AddError("Address line is required.");
        }
        else if (AddressLine.Length < 5)
        {
            result.AddError("Address line must be at least 5 characters long.");
        }
        else if (AddressLine.Length > 300)
        {
            result.AddError("Address line cannot exceed 300 characters.");
        }

        if (!string.IsNullOrWhiteSpace(City) && City.Length > 120)
        {
            result.AddError("City cannot exceed 120 characters.");
        }

        if (!string.IsNullOrWhiteSpace(Area) && Area.Length > 120)
        {
            result.AddError("Area cannot exceed 120 characters.");
        }

        if (Latitude.HasValue && (Latitude < -90 || Latitude > 90))
        {
            result.AddError("Latitude must be between -90 and 90.");
        }

        if (Longitude.HasValue && (Longitude < -180 || Longitude > 180))
        {
            result.AddError("Longitude must be between -180 and 180.");
        }

        return result;
    }
}
