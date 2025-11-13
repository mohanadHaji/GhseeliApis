using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.DTOs.ServiceOption;

public class CreateServiceOptionRequest
{
    [Required]
    public Guid ServiceId { get; set; }

    public Guid? CompanyId { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Range(1, 1440)]
    public int DurationMinutes { get; set; } = 30;

    [Range(0, 999999.99)]
    public decimal Price { get; set; }
}

public class UpdateServiceOptionRequest
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Range(1, 1440)]
    public int DurationMinutes { get; set; }

    [Range(0, 999999.99)]
    public decimal Price { get; set; }
}

public class ServiceOptionResponse
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public Guid? CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
}
