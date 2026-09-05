using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.DTOs.Address;

public class CreateAddressRequest
{
    [Required(ErrorMessage = "Address line is required")]
    [MaxLength(300)]
    public string AddressLine { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? City { get; set; }

    [MaxLength(120)]
    public string? Area { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public bool IsPrimary { get; set; } = false;
}

public class UpdateAddressRequest
{
    [Required]
    [MaxLength(300)]
    public string AddressLine { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? City { get; set; }

    [MaxLength(120)]
    public string? Area { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public bool IsPrimary { get; set; }
}

public class AddressResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string AddressLine { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Area { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsPrimary { get; set; }
    public string DisplayAddress => $"{AddressLine}, {City}".TrimEnd(',', ' ');
}
