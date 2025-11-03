using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a wallet transaction (deposit, withdrawal, payment)
/// </summary>
public class WalletTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid UserId { get; set; }
    
    /// <summary>
    /// Optional: Link to booking if this transaction is payment for a booking
    /// </summary>
    public Guid? BookingId { get; set; }
    
    [Precision(18, 2)]
    public decimal Amount { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public string? Description { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public Booking? Booking { get; set; }
}
