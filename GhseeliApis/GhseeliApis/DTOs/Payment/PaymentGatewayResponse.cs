namespace GhseeliApis.DTOs.Payment;

/// <summary>
/// Response from payment gateway operations
/// </summary>
public class PaymentGatewayResponse
{
    /// <summary>
    /// Indicates if the payment operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Transaction ID from the payment gateway
    /// </summary>
    public string? TransactionId { get; set; }

    /// <summary>
    /// Payment intent ID (for Stripe and similar providers)
    /// </summary>
    public string? PaymentIntentId { get; set; }

    /// <summary>
    /// Current status of the payment (e.g., succeeded, failed, requires_action)
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Amount processed in the smallest currency unit (e.g., cents)
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    /// Currency code (e.g., "usd", "eur")
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Error message if the operation failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code from the payment gateway
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Timestamp when the payment was processed
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Additional metadata from the payment gateway
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Indicates if the payment requires additional action (e.g., 3D Secure)
    /// </summary>
    public bool RequiresAction { get; set; }

    /// <summary>
    /// Client secret for completing payment actions on the frontend
    /// </summary>
    public string? ClientSecret { get; set; }
}
