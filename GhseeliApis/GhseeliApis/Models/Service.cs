using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a type of car washing service (e.g., Basic Wash, Premium Wash, Detailing)
/// </summary>
public class Service
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }

    // Navigation properties
    public ICollection<ServiceOption> Options { get; set; } = new List<ServiceOption>();
}
