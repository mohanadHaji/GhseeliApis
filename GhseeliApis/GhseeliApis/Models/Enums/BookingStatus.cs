namespace GhseeliApis.Models.Enums;

/// <summary>
/// Represents the current status of a booking
/// </summary>
public enum BookingStatus
{
    /// <summary>
    /// Booking has been created but not yet confirmed by the company
    /// </summary>
    Pending = 0,
    
    /// <summary>
    /// Company has confirmed the booking
    /// </summary>
    Confirmed = 1,
    
    /// <summary>
    /// Service is currently being performed
    /// </summary>
    InProgress = 2,
    
    /// <summary>
    /// Service has been completed successfully
    /// </summary>
    Completed = 3,
    
    /// <summary>
    /// Booking was cancelled by user or company
    /// </summary>
    Cancelled = 4,
    
    /// <summary>
    /// User did not show up for the scheduled service
    /// </summary>
    NoShow = 5
}
