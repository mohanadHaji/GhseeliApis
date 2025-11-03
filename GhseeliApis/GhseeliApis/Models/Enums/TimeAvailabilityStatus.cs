namespace GhseeliApis.Models.Enums;

/// <summary>
/// Represents the availability status of a time slot for a company
/// </summary>
public enum TimeAvailabilityStatus
{
    /// <summary>
    /// Time slot is available for booking
    /// </summary>
    Available = 0,
    
    /// <summary>
    /// Time slot is currently occupied with ongoing work
    /// </summary>
    Busy = 1,
    
    /// <summary>
    /// Time slot is not available (company closed, holiday, etc.)
    /// </summary>
    Unavailable = 2,
    
    /// <summary>
    /// Time slot is reserved but service hasn't started
    /// </summary>
    Reserved = 3
}
