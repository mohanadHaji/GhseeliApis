using System.ComponentModel.DataAnnotations;
using GhseeliApis.Interfaces;
using ValidationResult = GhseeliApis.Interfaces.ValidationResult;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a vehicle owned by a user
/// </summary>
public class Vehicle : IValidatable
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

    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };

        if (!string.IsNullOrWhiteSpace(Make) && Make.Length > 150)
        {
            result.AddError("Make cannot exceed 150 characters.");
        }

        if (!string.IsNullOrWhiteSpace(Model) && Model.Length > 150)
        {
            result.AddError("Model cannot exceed 150 characters.");
        }

        if (!string.IsNullOrWhiteSpace(Year))
        {
            if (Year.Length > 50)
            {
                result.AddError("Year cannot exceed 50 characters.");
            }
            
            if (int.TryParse(Year, out int yearNum))
            {
                if (yearNum < 1900 || yearNum > DateTime.UtcNow.Year + 2)
                {
                    result.AddError($"Year must be between 1900 and {DateTime.UtcNow.Year + 2}.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(LicensePlate) && LicensePlate.Length > 50)
        {
            result.AddError("License plate cannot exceed 50 characters.");
        }

        return result;
    }
}
