using System.ComponentModel.DataAnnotations;
using GhseeliApis.Models.Enums;

namespace GhseeliApis.DTOs.Payment;

/// <summary>
/// Request DTO for creating a payment
/// </summary>
public class CreatePaymentRequest
{
    [Required]
    public Guid BookingId { get; set; }

    [Required]
    [Range(0.01, 999999.99)]
    public decimal Amount { get; set; }

    [Required]
    public PaymentMethod Method { get; set; }

    public string? TransactionId { get; set; }
}

/// <summary>
/// Request DTO for updating payment status
/// </summary>
public class UpdatePaymentStatusRequest
{
    [Required]
    public PaymentStatus Status { get; set; }

    public string? TransactionId { get; set; }
}

/// <summary>
/// Response DTO for payment
/// </summary>
public class PaymentResponse
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public string? TransactionId { get; set; }
    public DateTime CreatedAt { get; set; }

    // Related data
    public string UserName { get; set; } = string.Empty;
    public string BookingInfo { get; set; } = string.Empty;
}
