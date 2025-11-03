using GhseeliApis.Models.Enums;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a booking for a car washing service
/// </summary>
public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ServiceOptionId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid AddressId { get; set; }

    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    /// <summary>
    /// Optional notes from user about the booking
    /// </summary>
    public string? Notes { get; set; }
    
    public bool IsPaid { get; set; } = false;

    // Navigation properties
    public User User { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public ServiceOption ServiceOption { get; set; } = null!;
    public Vehicle Vehicle { get; set; } = null!;
    public UserAddress Address { get; set; } = null!;
    public Payment? Payment { get; set; }
}
