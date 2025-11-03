using GhseeliApis.Models.Enums;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a company's availability schedule
/// Supports both recurring weekly slots and one-time specific dates
/// </summary>
public class CompanyAvailability
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid CompanyId { get; set; }

    /// <summary>
    /// Day of week for recurring availability (null for one-time slots)
    /// </summary>
    public DayOfWeek? DayOfWeek { get; set; }
    
    /// <summary>
    /// Specific date for one-time availability (null for recurring slots)
    /// </summary>
    public DateTime? SpecificDate { get; set; }
    
    public TimeSpan StartTime { get; set; }
    
    public TimeSpan EndTime { get; set; }
    
    public TimeAvailabilityStatus Status { get; set; } = TimeAvailabilityStatus.Available;

    // Navigation properties
    public Company Company { get; set; } = null!;
}
