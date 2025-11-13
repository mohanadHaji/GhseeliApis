using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.DTOs.Service;

public class CreateServiceRequest
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}

public class UpdateServiceRequest
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}

public class ServiceResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int OptionCount { get; set; }
}
