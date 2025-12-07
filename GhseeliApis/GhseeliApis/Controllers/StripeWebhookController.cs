using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models.Enums;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace GhseeliApis.Controllers;

/// <summary>
/// Controller for handling Stripe webhook events
/// </summary>
[ApiController]
[Route("api/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly IPaymentHandler _paymentHandler;
    private readonly IAppLogger _logger;
    private readonly IConfiguration _configuration;

    public StripeWebhookController(
        IPaymentHandler paymentHandler,
        IAppLogger logger,
        IConfiguration configuration)
    {
        _paymentHandler = paymentHandler;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Webhook endpoint for Stripe events
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> HandleWebhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        
        try
        {
            var webhookSecret = _configuration["Stripe:WebhookSecret"];
            
            if (string.IsNullOrEmpty(webhookSecret))
            {
                _logger.LogError("Stripe webhook secret is not configured");
                return BadRequest("Webhook secret not configured");
            }

            // Verify webhook signature
            var stripeSignature = Request.Headers["Stripe-Signature"].ToString();
            
            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    stripeSignature,
                    webhookSecret
                );
            }
            catch (StripeException ex)
            {
                _logger.LogError($"Stripe webhook signature verification failed: {ex.Message}");
                return BadRequest("Invalid signature");
            }

            _logger.LogInfo($"Stripe webhook received: {stripeEvent.Type}, ID: {stripeEvent.Id}");

            // Handle different event types
            switch (stripeEvent.Type)
            {
                case Events.PaymentIntentSucceeded:
                    await HandlePaymentIntentSucceeded(stripeEvent);
                    break;

                case Events.PaymentIntentPaymentFailed:
                    await HandlePaymentIntentFailed(stripeEvent);
                    break;

                case Events.ChargeRefunded:
                    await HandleChargeRefunded(stripeEvent);
                    break;

                case Events.PaymentIntentCanceled:
                    await HandlePaymentIntentCanceled(stripeEvent);
                    break;

                default:
                    _logger.LogInfo($"Unhandled webhook event type: {stripeEvent.Type}");
                    break;
            }

            return Ok(new { received = true });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error processing Stripe webhook: {ex.Message}", ex);
            
            // Return 200 to prevent Stripe from retrying (we've logged the error)
            // In production, you might want to return 500 for transient errors
            return Ok(new { received = true, error = "Internal error occurred" });
        }
    }

    private async Task HandlePaymentIntentSucceeded(Event stripeEvent)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent == null)
        {
            _logger.LogWarning("PaymentIntent object is null in payment_intent.succeeded event");
            return;
        }

        _logger.LogInfo($"Payment intent succeeded: {paymentIntent.Id}, Amount: {paymentIntent.Amount}");

        // Extract booking ID from metadata
        if (paymentIntent.Metadata.TryGetValue("booking_id", out var bookingIdStr) &&
            Guid.TryParse(bookingIdStr, out var bookingId))
        {
            // Find payment by booking ID
            var payment = await _paymentHandler.GetByBookingIdAsync(bookingId);
            
            if (payment == null)
            {
                _logger.LogWarning($"Payment not found for booking {bookingId}");
                return;
            }

            // Update payment status if not already completed
            if (payment.Status != PaymentStatus.Completed)
            {
                _logger.LogInfo($"Updating payment {payment.Id} to Completed via webhook");
                await _paymentHandler.UpdateStatusAsync(payment.Id, PaymentStatus.Completed);
            }
            else
            {
                _logger.LogInfo($"Payment {payment.Id} already completed, skipping update");
            }
        }
        else
        {
            _logger.LogWarning($"No valid booking_id in PaymentIntent metadata: {paymentIntent.Id}");
        }
    }

    private async Task HandlePaymentIntentFailed(Event stripeEvent)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent == null)
        {
            _logger.LogWarning("PaymentIntent object is null in payment_intent.payment_failed event");
            return;
        }

        _logger.LogWarning($"Payment intent failed: {paymentIntent.Id}, Reason: {paymentIntent.LastPaymentError?.Message}");

        // Extract booking ID from metadata
        if (paymentIntent.Metadata.TryGetValue("booking_id", out var bookingIdStr) &&
            Guid.TryParse(bookingIdStr, out var bookingId))
        {
            // Find payment by booking ID
            var payment = await _paymentHandler.GetByBookingIdAsync(bookingId);
            
            if (payment == null)
            {
                _logger.LogWarning($"Payment not found for booking {bookingId}");
                return;
            }

            // Update payment status to Failed
            if (payment.Status == PaymentStatus.Pending)
            {
                _logger.LogInfo($"Updating payment {payment.Id} to Failed via webhook");
                await _paymentHandler.UpdateStatusAsync(payment.Id, PaymentStatus.Failed);
            }
            else
            {
                _logger.LogInfo($"Payment {payment.Id} status is {payment.Status}, skipping update");
            }
        }
        else
        {
            _logger.LogWarning($"No valid booking_id in PaymentIntent metadata: {paymentIntent.Id}");
        }
    }

    private async Task HandleChargeRefunded(Event stripeEvent)
    {
        var charge = stripeEvent.Data.Object as Charge;
        if (charge == null)
        {
            _logger.LogWarning("Charge object is null in charge.refunded event");
            return;
        }

        _logger.LogInfo($"Charge refunded: {charge.Id}, Amount: {charge.AmountRefunded}");

        // Find payment by transaction ID (charge ID)
        var allPayments = await _paymentHandler.GetAllAsync();
        var payment = allPayments.FirstOrDefault(p => p.TransactionId == charge.Id);

        if (payment == null)
        {
            _logger.LogWarning($"Payment not found for charge {charge.Id}");
            return;
        }

        // Update payment status to Refunded if not already
        if (payment.Status != PaymentStatus.Refunded)
        {
            _logger.LogInfo($"Updating payment {payment.Id} to Refunded via webhook");
            await _paymentHandler.UpdateStatusAsync(payment.Id, PaymentStatus.Refunded);
        }
        else
        {
            _logger.LogInfo($"Payment {payment.Id} already refunded, skipping update");
        }
    }

    private async Task HandlePaymentIntentCanceled(Event stripeEvent)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent == null)
        {
            _logger.LogWarning("PaymentIntent object is null in payment_intent.canceled event");
            return;
        }

        _logger.LogInfo($"Payment intent canceled: {paymentIntent.Id}");

        // Extract booking ID from metadata
        if (paymentIntent.Metadata.TryGetValue("booking_id", out var bookingIdStr) &&
            Guid.TryParse(bookingIdStr, out var bookingId))
        {
            // Find payment by booking ID
            var payment = await _paymentHandler.GetByBookingIdAsync(bookingId);
            
            if (payment == null)
            {
                _logger.LogWarning($"Payment not found for booking {bookingId}");
                return;
            }

            // Update payment status to Failed (treat cancellation as failure)
            if (payment.Status == PaymentStatus.Pending)
            {
                _logger.LogInfo($"Updating payment {payment.Id} to Failed (canceled) via webhook");
                await _paymentHandler.UpdateStatusAsync(payment.Id, PaymentStatus.Failed);
            }
        }
        else
        {
            _logger.LogWarning($"No valid booking_id in PaymentIntent metadata: {paymentIntent.Id}");
        }
    }
}
