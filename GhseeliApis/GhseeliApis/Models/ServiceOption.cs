using System.ComponentModel.DataAnnotations;
using GhseeliApis.Interfaces;
using Microsoft.EntityFrameworkCore;
using ValidationResult = GhseeliApis.Interfaces.ValidationResult;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a specific service offering with pricing and duration
/// Can be company-specific or generic
/// </summary>
public class ServiceOption : IValidatable
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

    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(Name))
        {
            result.AddError("Service option name is required.");
        }
        else if (Name.Length < 2)
        {
            result.AddError("Service option name must be at least 2 characters long.");
        }
        else if (Name.Length > 150)
        {
            result.AddError("Service option name cannot exceed 150 characters.");
        }

        if (DurationMinutes <= 0)
        {
            result.AddError("Duration must be greater than 0 minutes.");
        }
        else if (DurationMinutes > 1440) // 24 hours
        {
            result.AddError("Duration cannot exceed 24 hours (1440 minutes).");
        }

        if (Price < 0)
        {
            result.AddError("Price cannot be negative.");
        }
        else if (Price > 999999.99m)
        {
            result.AddError("Price cannot exceed 999,999.99.");
        }

        return result;
    }
}
