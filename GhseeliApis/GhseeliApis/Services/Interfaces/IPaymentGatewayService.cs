using GhseeliApis.DTOs.Payment;

namespace GhseeliApis.Services.Interfaces;

/// <summary>
/// Interface for payment gateway operations supporting multiple payment providers
/// </summary>
public interface IPaymentGatewayService
{
    /// <summary>
    /// Process a payment using a payment method token
    /// </summary>
    /// <param name="amount">Amount to charge in the smallest currency unit (e.g., cents)</param>
    /// <param name="currency">Currency code (e.g., "usd", "eur")</param>
    /// <param name="paymentMethodId">Payment method ID/token from frontend</param>
    /// <param name="description">Optional payment description</param>
    /// <param name="metadata">Optional metadata to attach to the payment</param>
    /// <returns>Payment gateway response with transaction details</returns>
    Task<PaymentGatewayResponse> ProcessPaymentAsync(
        long amount,
        string currency,
        string paymentMethodId,
        string? description = null,
        Dictionary<string, string>? metadata = null);

    /// <summary>
    /// Refund a previously processed payment
    /// </summary>
    /// <param name="transactionId">Original transaction ID to refund</param>
    /// <param name="amount">Optional partial refund amount (null for full refund)</param>
    /// <param name="reason">Optional refund reason</param>
    /// <returns>Payment gateway response for the refund</returns>
    Task<PaymentGatewayResponse> RefundPaymentAsync(
        string transactionId,
        long? amount = null,
        string? reason = null);

    /// <summary>
    /// Capture a previously authorized payment
    /// </summary>
    /// <param name="paymentIntentId">Payment intent ID to capture</param>
    /// <param name="amount">Optional amount to capture (null to capture full authorized amount)</param>
    /// <returns>Payment gateway response for the capture</returns>
    Task<PaymentGatewayResponse> CapturePaymentAsync(
        string paymentIntentId,
        long? amount = null);
}
