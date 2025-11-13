using GhseeliApis.DTOs.Payment;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentHandler _paymentHandler;
    private readonly IAppLogger _logger;

    public PaymentsController(IPaymentHandler paymentHandler, IAppLogger logger)
    {
        _paymentHandler = paymentHandler;
        _logger = logger;
    }

    /// <summary>
    /// Get all payments (admin only - would need authorization)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            _logger.LogInfo("GET /api/payments");
            
            var payments = await _paymentHandler.GetAllAsync();
            var response = payments.Select(p => MapToResponse(p));

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting all payments", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Get payment by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            _logger.LogInfo($"GET /api/payments/{id}");
            
            var payment = await _paymentHandler.GetByIdAsync(id);
            if (payment == null)
                return NotFound();

            var response = MapToResponse(payment);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting payment {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Get payments for current user
    /// </summary>
    [HttpGet("my-payments")]
    public async Task<IActionResult> GetMyPayments()
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"GET /api/payments/my-payments for user {userId}");
            
            var payments = await _paymentHandler.GetByUserIdAsync(userId);
            var response = payments.Select(p => MapToResponse(p));

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting user payments", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Get payment for a specific booking
    /// </summary>
    [HttpGet("booking/{bookingId:guid}")]
    public async Task<IActionResult> GetByBookingId(Guid bookingId)
    {
        try
        {
            _logger.LogInfo($"GET /api/payments/booking/{bookingId}");
            
            var payment = await _paymentHandler.GetByBookingIdAsync(bookingId);
            if (payment == null)
                return NotFound();

            var response = MapToResponse(payment);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting payment for booking {bookingId}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Create a new payment
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request)
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"POST /api/payments - Creating payment for booking {request.BookingId}");

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("POST /api/payments - Invalid model state");
                return BadRequest(ModelState);
            }

            var payment = new Payment
            {
                BookingId = request.BookingId,
                Amount = request.Amount,
                Method = request.Method,
                TransactionId = request.TransactionId
            };

            var created = await _paymentHandler.CreateAsync(payment, userId);

            var response = MapToResponse(created);
            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Payment creation failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error creating payment", ex);
            return StatusCode(500, "An error occurred while creating the payment");
        }
    }

    /// <summary>
    /// Update payment status (admin/system action)
    /// </summary>
    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdatePaymentStatusRequest request)
    {
        try
        {
            _logger.LogInfo($"PUT /api/payments/{id}/status to {request.Status}");

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("PUT /api/payments/status - Invalid model state");
                return BadRequest(ModelState);
            }

            var updated = await _paymentHandler.UpdateStatusAsync(id, request.Status);
            if (updated == null)
                return NotFound();

            var response = MapToResponse(updated);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Payment status update failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating payment {id} status", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Process a refund
    /// </summary>
    [HttpPost("{id:guid}/refund")]
    public async Task<IActionResult> Refund(Guid id)
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"POST /api/payments/{id}/refund by user {userId}");

            var refunded = await _paymentHandler.ProcessRefundAsync(id, userId);
            if (refunded == null)
                return NotFound();

            var response = MapToResponse(refunded);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Payment refund failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error processing refund for payment {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    private static PaymentResponse MapToResponse(Payment payment)
    {
        return new PaymentResponse
        {
            Id = payment.Id,
            BookingId = payment.BookingId,
            UserId = payment.UserId,
            Amount = payment.Amount,
            Method = payment.Method,
            Status = payment.Status,
            TransactionId = payment.TransactionId,
            CreatedAt = payment.CreatedAt,
            UserName = payment.User?.FullName ?? string.Empty,
            BookingInfo = payment.Booking != null 
                ? $"Booking #{payment.Booking.Id.ToString().Substring(0, 8)}" 
                : string.Empty
        };
    }
}
