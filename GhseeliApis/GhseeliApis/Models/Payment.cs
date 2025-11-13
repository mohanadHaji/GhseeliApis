using System.ComponentModel.DataAnnotations;
using GhseeliApis.Interfaces;
using GhseeliApis.Models.Enums;
using Microsoft.EntityFrameworkCore;
using ValidationResult = GhseeliApis.Interfaces.ValidationResult;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a payment transaction for a booking
/// </summary>
public class Payment : IValidatable
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

    public ValidationResult Validate()
    {
        var result = new ValidationResult { IsValid = true };

        if (Amount <= 0)
        {
            result.AddError("Payment amount must be greater than zero.");
        }
        else if (Amount > 999999.99m)
        {
            result.AddError("Payment amount cannot exceed 999,999.99.");
        }

        if (BookingId == Guid.Empty)
        {
            result.AddError("Booking ID is required.");
        }

        if (UserId == Guid.Empty)
        {
            result.AddError("User ID is required.");
        }

        if (!Enum.IsDefined(typeof(PaymentMethod), Method))
        {
            result.AddError("Invalid payment method.");
        }

        if (!Enum.IsDefined(typeof(PaymentStatus), Status))
        {
            result.AddError("Invalid payment status.");
        }

        if (!string.IsNullOrWhiteSpace(TransactionId) && TransactionId.Length > 200)
        {
            result.AddError("Transaction ID cannot exceed 200 characters.");
        }

        return result;
    }
}
