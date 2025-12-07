using GhseeliApis.DTOs.Payment;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Stripe;

namespace GhseeliApis.Services;

/// <summary>
/// Stripe implementation of payment gateway service
/// </summary>
public class StripePaymentService : IPaymentGatewayService
{
    private readonly IAppLogger _logger;
    private readonly string _secretKey;

    public StripePaymentService(IConfiguration configuration, IAppLogger logger)
    {
        _logger = logger;
        _secretKey = configuration["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe SecretKey is not configured");

        // Set Stripe API key
        StripeConfiguration.ApiKey = _secretKey;
    }

    /// <inheritdoc />
    public async Task<PaymentGatewayResponse> ProcessPaymentAsync(
        long amount,
        string currency,
        string paymentMethodId,
        string? description = null,
        Dictionary<string, string>? metadata = null)
    {
        try
        {
            _logger.LogInfo($"Processing Stripe payment: Amount={amount}, Currency={currency}, PaymentMethod={paymentMethodId}");

            // Create payment intent
            var paymentIntentService = new PaymentIntentService();
            var options = new PaymentIntentCreateOptions
            {
                Amount = amount,
                Currency = currency.ToLower(),
                PaymentMethod = paymentMethodId,
                Description = description,
                Metadata = metadata,
                Confirm = true, // Automatically confirm the payment
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never" // Disable redirect-based payment methods
                }
            };

            var paymentIntent = await paymentIntentService.CreateAsync(options);

            _logger.LogInfo($"Stripe payment processed: Status={paymentIntent.Status}, IntentId={paymentIntent.Id}");

            return new PaymentGatewayResponse
            {
                Success = paymentIntent.Status == "succeeded",
                TransactionId = paymentIntent.LatestChargeId,
                PaymentIntentId = paymentIntent.Id,
                Status = paymentIntent.Status,
                Amount = paymentIntent.Amount,
                Currency = paymentIntent.Currency,
                ProcessedAt = DateTime.UtcNow,
                Metadata = paymentIntent.Metadata,
                RequiresAction = paymentIntent.Status == "requires_action",
                ClientSecret = paymentIntent.Status == "requires_action" ? paymentIntent.ClientSecret : null
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError($"Stripe payment failed: {ex.Message}");

            return new PaymentGatewayResponse
            {
                Success = false,
                Status = "failed",
                ErrorMessage = ex.Message,
                ErrorCode = ex.StripeError?.Code,
                ProcessedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Payment processing error: {ex.Message}");

            return new PaymentGatewayResponse
            {
                Success = false,
                Status = "failed",
                ErrorMessage = "An unexpected error occurred while processing the payment",
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    /// <inheritdoc />
    public async Task<PaymentGatewayResponse> RefundPaymentAsync(
        string transactionId,
        long? amount = null,
        string? reason = null)
    {
        try
        {
            _logger.LogInfo($"Processing Stripe refund: ChargeId={transactionId}, Amount={amount ?? 0}, Reason={reason}");

            var refundService = new RefundService();
            var options = new RefundCreateOptions
            {
                Charge = transactionId,
                Amount = amount,
                Reason = reason
            };

            var refund = await refundService.CreateAsync(options);

            _logger.LogInfo($"Stripe refund processed: Status={refund.Status}, RefundId={refund.Id}");

            return new PaymentGatewayResponse
            {
                Success = refund.Status == "succeeded",
                TransactionId = refund.Id,
                Status = refund.Status,
                Amount = refund.Amount,
                Currency = refund.Currency,
                ProcessedAt = DateTime.UtcNow
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError($"Stripe refund failed: {ex.Message}");

            return new PaymentGatewayResponse
            {
                Success = false,
                Status = "failed",
                ErrorMessage = ex.Message,
                ErrorCode = ex.StripeError?.Code,
                ProcessedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Refund processing error: {ex.Message}");

            return new PaymentGatewayResponse
            {
                Success = false,
                Status = "failed",
                ErrorMessage = "An unexpected error occurred while processing the refund",
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    /// <inheritdoc />
    public async Task<PaymentGatewayResponse> CapturePaymentAsync(
        string paymentIntentId,
        long? amount = null)
    {
        try
        {
            _logger.LogInfo($"Capturing Stripe payment: IntentId={paymentIntentId}, Amount={amount ?? 0}");

            var paymentIntentService = new PaymentIntentService();
            var options = new PaymentIntentCaptureOptions
            {
                AmountToCapture = amount
            };

            var paymentIntent = await paymentIntentService.CaptureAsync(paymentIntentId, options);

            _logger.LogInfo($"Stripe payment captured: Status={paymentIntent.Status}, IntentId={paymentIntent.Id}");

            return new PaymentGatewayResponse
            {
                Success = paymentIntent.Status == "succeeded",
                TransactionId = paymentIntent.LatestChargeId,
                PaymentIntentId = paymentIntent.Id,
                Status = paymentIntent.Status,
                Amount = paymentIntent.Amount,
                Currency = paymentIntent.Currency,
                ProcessedAt = DateTime.UtcNow,
                Metadata = paymentIntent.Metadata
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError($"Stripe capture failed: {ex.Message}");

            return new PaymentGatewayResponse
            {
                Success = false,
                Status = "failed",
                ErrorMessage = ex.Message,
                ErrorCode = ex.StripeError?.Code,
                ProcessedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Capture processing error: {ex.Message}");

            return new PaymentGatewayResponse
            {
                Success = false,
                Status = "failed",
                ErrorMessage = "An unexpected error occurred while capturing the payment",
                ProcessedAt = DateTime.UtcNow
            };
        }
    }
}
