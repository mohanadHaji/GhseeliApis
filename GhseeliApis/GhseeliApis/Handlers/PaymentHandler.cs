using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Repositories.Interfaces;

namespace GhseeliApis.Handlers;

/// <summary>
/// Handler for payment-related business logic
/// </summary>
public class PaymentHandler : IPaymentHandler
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IAppLogger _logger;

    public PaymentHandler(
        IPaymentRepository paymentRepository,
        IBookingRepository bookingRepository,
        IAppLogger logger)
    {
        _paymentRepository = paymentRepository;
        _bookingRepository = bookingRepository;
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

        var created = await _paymentRepository.AddAsync(payment);
        _logger.LogInfo($"PaymentHandler: Payment created successfully - ID {created.Id}");

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

        // Process refund
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
