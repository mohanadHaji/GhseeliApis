using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.DTOs.Vehicle;

public class CreateVehicleRequest
{
    [MaxLength(150)]
    public string? Make { get; set; }

    [MaxLength(150)]
    public string? Model { get; set; }

    [MaxLength(50)]
    public string? Year { get; set; }

    [MaxLength(50)]
    public string? LicensePlate { get; set; }

    public string? Color { get; set; }
}

public class UpdateVehicleRequest
{
    [MaxLength(150)]
    public string? Make { get; set; }

    [MaxLength(150)]
    public string? Model { get; set; }

    [MaxLength(50)]
    public string? Year { get; set; }

    [MaxLength(50)]
    public string? LicensePlate { get; set; }

    public string? Color { get; set; }
}

public class VehicleResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Year { get; set; }
    public string? LicensePlate { get; set; }
    public string? Color { get; set; }
    public string DisplayName => $"{Year} {Make} {Model}".Trim();
}
