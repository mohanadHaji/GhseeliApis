using GhseeliApis.Interfaces;
using GhseeliApis.Models.Enums;
using ValidationResult = GhseeliApis.Interfaces.ValidationResult;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a booking for a car washing service
/// </summary>
public class Booking : IValidatable
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

    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };

        // Validate dates
        if (StartDateTime == default)
        {
            result.AddError("Start date time is required.");
        }
        else if (StartDateTime < DateTime.UtcNow.AddMinutes(-5)) // Allow 5 min grace for clock differences
        {
            result.AddError("Start date time cannot be in the past.");
        }
        else if (StartDateTime > DateTime.UtcNow.AddYears(1))
        {
            result.AddError("Start date time cannot be more than 1 year in the future.");
        }

        if (EndDateTime == default)
        {
            result.AddError("End date time is required.");
        }
        else if (EndDateTime <= StartDateTime)
        {
            result.AddError("End date time must be after start date time.");
        }

        var duration = (EndDateTime - StartDateTime).TotalMinutes;
        if (duration < 15)
        {
            result.AddError("Booking duration must be at least 15 minutes.");
        }
        else if (duration > 480) // 8 hours
        {
            result.AddError("Booking duration cannot exceed 8 hours.");
        }

        // Validate IDs are not empty
        if (UserId == Guid.Empty)
        {
            result.AddError("User ID is required.");
        }

        if (CompanyId == Guid.Empty)
        {
            result.AddError("Company ID is required.");
        }

        if (ServiceOptionId == Guid.Empty)
        {
            result.AddError("Service option ID is required.");
        }

        if (VehicleId == Guid.Empty)
        {
            result.AddError("Vehicle ID is required.");
        }

        if (AddressId == Guid.Empty)
        {
            result.AddError("Address ID is required.");
        }

        // Validate notes length
        if (!string.IsNullOrWhiteSpace(Notes) && Notes.Length > 500)
        {
            result.AddError("Notes cannot exceed 500 characters.");
        }

        return result;
    }
}
