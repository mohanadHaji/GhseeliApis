using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.DTOs.Company;

public class CreateCompanyRequest
{
    [Required(ErrorMessage = "Company name is required")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Phone { get; set; }

    public string? Description { get; set; }

    [MaxLength(200)]
    public string? ServiceAreaDescription { get; set; }
}

public class UpdateCompanyRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Phone { get; set; }

    public string? Description { get; set; }

    [MaxLength(200)]
    public string? ServiceAreaDescription { get; set; }
}

public class CompanyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public string? ServiceAreaDescription { get; set; }
}

public class CompanyListResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ServiceAreaDescription { get; set; }
    public int ServiceOptionCount { get; set; }
}
