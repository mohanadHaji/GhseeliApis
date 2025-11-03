using System.ComponentModel.DataAnnotations;
using GhseeliApis.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a payment transaction for a booking
/// </summary>
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid BookingId { get; set; }
    public Guid UserId { get; set; }

    [Precision(18, 2)]
    public decimal Amount { get; set; }
    
    public PaymentMethod Method { get; set; } = PaymentMethod.Card;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>
    /// Transaction ID from payment gateway/provider
    /// </summary>
    [MaxLength(200)]
    public string? TransactionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Booking Booking { get; set; } = null!;
    public User User { get; set; } = null!;
}
