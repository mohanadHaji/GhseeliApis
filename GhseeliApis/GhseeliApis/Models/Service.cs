using System.ComponentModel.DataAnnotations;
using GhseeliApis.Interfaces;
using ValidationResult = GhseeliApis.Interfaces.ValidationResult;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a type of car washing service (e.g., Basic Wash, Premium Wash, Detailing)
/// </summary>
public class Service : IValidatable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }

    // Navigation properties
    public ICollection<ServiceOption> Options { get; set; } = new List<ServiceOption>();

    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(Name))
        {
            result.AddError("Service name is required.");
        }
        else if (Name.Length < 2)
        {
            result.AddError("Service name must be at least 2 characters long.");
        }
        else if (Name.Length > 150)
        {
            result.AddError("Service name cannot exceed 150 characters.");
        }

        return result;
    }
}
