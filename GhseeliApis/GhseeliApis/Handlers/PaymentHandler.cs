using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Interfaces;

namespace GhseeliApis.Handlers;

/// <summary>
/// Handler for payment-related business logic
/// </summary>
public class PaymentHandler : IPaymentHandler
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IPaymentGatewayService _paymentGateway;
    private readonly IAppLogger _logger;

    public PaymentHandler(
        IPaymentRepository paymentRepository,
        IBookingRepository bookingRepository,
        IPaymentGatewayService paymentGateway,
        IAppLogger logger)
    {
        _paymentRepository = paymentRepository;
        _bookingRepository = bookingRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<List<Payment>> GetAllAsync()
    {
        _logger.LogInfo("PaymentHandler: Getting all payments");
        return await _paymentRepository.GetAllAsync();
    }

    public async Task<Payment?> GetByIdAsync(Guid id)
    {
        _logger.LogInfo($"PaymentHandler: Getting payment with ID {id}");
        return await _paymentRepository.GetByIdAsync(id);
    }

    public async Task<List<Payment>> GetByUserIdAsync(Guid userId)
    {
        _logger.LogInfo($"PaymentHandler: Getting payments for user {userId}");
        return await _paymentRepository.GetByUserIdAsync(userId);
    }

    public async Task<Payment?> GetByBookingIdAsync(Guid bookingId)
    {
        _logger.LogInfo($"PaymentHandler: Getting payment for booking {bookingId}");
        return await _paymentRepository.GetByBookingIdAsync(bookingId);
    }

    public async Task<Payment> CreateAsync(Payment payment, Guid userId)
    {
        _logger.LogInfo($"PaymentHandler: Creating payment for user {userId}, booking {payment.BookingId}");

        // Set UserId before validation
        payment.UserId = userId;
        payment.Status = PaymentStatus.Pending;
        payment.CreatedAt = DateTime.UtcNow;

        // Validate payment data
        var validationResult = payment.Validate();
        if (!validationResult.IsValid)
        {
            var errors = string.Join(", ", validationResult.Errors);
            _logger.LogWarning($"PaymentHandler: Payment validation failed - {errors}");
            throw new InvalidOperationException($"Payment validation failed: {errors}");
        }

        // Verify booking exists and belongs to user
        var booking = await _bookingRepository.GetByIdAsync(payment.BookingId);
        if (booking == null)
        {
            _logger.LogWarning($"PaymentHandler: Booking {payment.BookingId} not found");
            throw new InvalidOperationException("Booking not found.");
        }

        if (booking.UserId != userId)
        {
            _logger.LogWarning($"PaymentHandler: User {userId} does not own booking {payment.BookingId}");
            throw new InvalidOperationException("You can only create payments for your own bookings.");
        }

        // Check if payment already exists for this booking
        var existingPayment = await _paymentRepository.GetByBookingIdAsync(payment.BookingId);
        if (existingPayment != null)
        {
            _logger.LogWarning($"PaymentHandler: Payment already exists for booking {payment.BookingId}");
            throw new InvalidOperationException("Payment already exists for this booking.");
        }

        // Process credit card payment through Stripe if PaymentMethodId is provided
        if (payment.Method == PaymentMethod.Card && !string.IsNullOrWhiteSpace(payment.PaymentMethodId))
        {
            _logger.LogInfo($"PaymentHandler: Processing credit card payment through Stripe for booking {payment.BookingId}");

            try
            {
                // Convert amount to cents (Stripe uses smallest currency unit)
                var amountInCents = (long)(payment.Amount * 100);

                // Process payment through Stripe
                var stripeResult = await _paymentGateway.ProcessPaymentAsync(
                    amount: amountInCents,
                    currency: "usd",
                    paymentMethodId: payment.PaymentMethodId,
                    description: $"Payment for booking {payment.BookingId}",
                    metadata: new Dictionary<string, string>
                    {
                        { "booking_id", payment.BookingId.ToString() },
                        { "user_id", userId.ToString() }
                    }
                );

                // Store Stripe transaction details
                payment.TransactionId = stripeResult.TransactionId;
                payment.PaymentIntentId = stripeResult.PaymentIntentId;

                if (stripeResult.Success)
                {
                    payment.Status = PaymentStatus.Completed;
                    _logger.LogInfo($"PaymentHandler: Stripe payment successful - Transaction ID: {stripeResult.TransactionId}");

                    // Mark booking as paid
                    booking.IsPaid = true;
                    await _bookingRepository.UpdateAsync(booking);
                    _logger.LogInfo($"PaymentHandler: Booking {booking.Id} marked as paid");
                }
                else
                {
                    payment.Status = PaymentStatus.Failed;
                    _logger.LogWarning($"PaymentHandler: Stripe payment failed - {stripeResult.ErrorMessage}");
                    
                    // Store error information in a way that can be communicated to the user
                    var errorMessage = stripeResult.ErrorMessage ?? "Payment processing failed";
                    throw new InvalidOperationException($"Payment failed: {errorMessage}");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError($"PaymentHandler: Stripe payment processing error - {ex.Message}");
                payment.Status = PaymentStatus.Failed;
                throw new InvalidOperationException($"Payment processing error: {ex.Message}", ex);
            }
        }
        else if (payment.Method == PaymentMethod.Card && string.IsNullOrWhiteSpace(payment.PaymentMethodId))
        {
            _logger.LogWarning($"PaymentHandler: Credit card payment requires PaymentMethodId");
            throw new InvalidOperationException("Credit card payments require a valid payment method ID from Stripe.");
        }
        else
        {
            // For non-card payments (Cash, Wallet), just create the record
            _logger.LogInfo($"PaymentHandler: Creating non-card payment record (Method: {payment.Method})");
        }

        var created = await _paymentRepository.AddAsync(payment);
        _logger.LogInfo($"PaymentHandler: Payment created successfully - ID {created.Id}, Status: {created.Status}");

        return created;
    }

    public async Task<Payment?> UpdateStatusAsync(Guid id, PaymentStatus status)
    {
        _logger.LogInfo($"PaymentHandler: Updating payment {id} status to {status}");

        var payment = await _paymentRepository.GetByIdAsync(id);
        if (payment == null)
        {
            _logger.LogWarning($"PaymentHandler: Payment {id} not found");
            return null;
        }

        // Validate status transition
        if (payment.Status == PaymentStatus.Completed && status != PaymentStatus.Refunded)
        {
            _logger.LogWarning($"PaymentHandler: Cannot change status from Completed to {status}");
            throw new InvalidOperationException("Completed payments can only be refunded.");
        }

        if (payment.Status == PaymentStatus.Refunded)
        {
            _logger.LogWarning($"PaymentHandler: Cannot change status of refunded payment");
            throw new InvalidOperationException("Cannot modify refunded payments.");
        }

        payment.Status = status;

        // If payment is completed, mark booking as paid
        if (status == PaymentStatus.Completed)
        {
            var booking = await _bookingRepository.GetByIdAsync(payment.BookingId);
            if (booking != null)
            {
                booking.IsPaid = true;
                await _bookingRepository.UpdateAsync(booking);
                _logger.LogInfo($"PaymentHandler: Booking {booking.Id} marked as paid");
            }
        }

        var updated = await _paymentRepository.UpdateAsync(payment);
        _logger.LogInfo($"PaymentHandler: Payment {id} status updated to {status}");

        return updated;
    }

    public async Task<Payment?> ProcessRefundAsync(Guid id, Guid userId)
    {
        _logger.LogInfo($"PaymentHandler: Processing refund for payment {id} by user {userId}");

        var payment = await _paymentRepository.GetByIdAsync(id);
        if (payment == null)
        {
            _logger.LogWarning($"PaymentHandler: Payment {id} not found");
            return null;
        }

        // Verify user owns the payment
        if (payment.UserId != userId)
        {
            _logger.LogWarning($"PaymentHandler: User {userId} does not own payment {id}");
            throw new InvalidOperationException("You can only refund your own payments.");
        }

        // Verify payment is completed
        if (payment.Status != PaymentStatus.Completed)
        {
            _logger.LogWarning($"PaymentHandler: Payment {id} is not completed (status: {payment.Status})");
            throw new InvalidOperationException("Only completed payments can be refunded.");
        }

        // Process refund through Stripe if this was a credit card payment
        if (payment.Method == PaymentMethod.Card && !string.IsNullOrWhiteSpace(payment.TransactionId))
        {
            _logger.LogInfo($"PaymentHandler: Processing Stripe refund for transaction {payment.TransactionId}");

            try
            {
                var refundResult = await _paymentGateway.RefundPaymentAsync(
                    transactionId: payment.TransactionId,
                    amount: null, // Full refund
                    reason: "requested_by_customer"
                );

                if (!refundResult.Success)
                {
                    _logger.LogWarning($"PaymentHandler: Stripe refund failed - {refundResult.ErrorMessage}");
                    throw new InvalidOperationException($"Refund failed: {refundResult.ErrorMessage ?? "Unknown error"}");
                }

                _logger.LogInfo($"PaymentHandler: Stripe refund successful - Refund ID: {refundResult.TransactionId}");
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError($"PaymentHandler: Stripe refund processing error - {ex.Message}");
                throw new InvalidOperationException($"Refund processing error: {ex.Message}", ex);
            }
        }
        else
        {
            _logger.LogInfo($"PaymentHandler: Processing non-Stripe refund (Method: {payment.Method})");
        }

        // Update payment status
        payment.Status = PaymentStatus.Refunded;

        // Update booking paid status
        var booking = await _bookingRepository.GetByIdAsync(payment.BookingId);
        if (booking != null)
        {
            booking.IsPaid = false;
            await _bookingRepository.UpdateAsync(booking);
            _logger.LogInfo($"PaymentHandler: Booking {booking.Id} marked as unpaid");
        }

        var updated = await _paymentRepository.UpdateAsync(payment);
        _logger.LogInfo($"PaymentHandler: Payment {id} refunded successfully");

        return updated;
    }
}
