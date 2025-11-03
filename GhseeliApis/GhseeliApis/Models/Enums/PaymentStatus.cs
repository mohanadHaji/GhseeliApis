namespace GhseeliApis.Models.Enums;

/// <summary>
/// Represents the status of a payment transaction
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// Payment has been initiated but not yet processed
    /// </summary>
    Pending = 0,
    
    /// <summary>
    /// Payment was successfully processed
    /// </summary>
    Completed = 1,
    
    /// <summary>
    /// Payment processing failed
    /// </summary>
    Failed = 2,
    
    /// <summary>
    /// Payment was refunded to the user
    /// </summary>
    Refunded = 3
}
