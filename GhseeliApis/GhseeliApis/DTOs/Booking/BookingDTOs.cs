using System.ComponentModel.DataAnnotations;
using GhseeliApis.Models.Enums;

namespace GhseeliApis.DTOs.Booking;

public class CreateBookingRequest
{
    [Required]
    public Guid VehicleId { get; set; }

    [Required]
    public Guid ServiceOptionId { get; set; }

    [Required]
    public Guid AddressId { get; set; }

    [Required]
    public DateTime StartDateTime { get; set; }

    public string? Notes { get; set; }
}

public class UpdateBookingRequest
{
    public DateTime? StartDateTime { get; set; }
    public string? Notes { get; set; }
}

public class BookingResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ServiceOptionId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid AddressId { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public BookingStatus Status { get; set; }
    public string? Notes { get; set; }
    public bool IsPaid { get; set; }

    // Related data
    public string CompanyName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string VehicleInfo { get; set; } = string.Empty;
    public string AddressInfo { get; set; } = string.Empty;
}
