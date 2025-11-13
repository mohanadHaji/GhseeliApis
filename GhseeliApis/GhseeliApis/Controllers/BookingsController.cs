using GhseeliApis.DTOs.Booking;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly IBookingHandler _bookingHandler;
    private readonly IAppLogger _logger;

    public BookingsController(IBookingHandler bookingHandler, IAppLogger logger)
    {
        _bookingHandler = bookingHandler;
        _logger = logger;
    }

    /// <summary>
    /// Get all bookings for current user
    /// </summary>
    [HttpGet("my-bookings")]
    public async Task<IActionResult> GetMyBookings()
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"GET /api/bookings/my-bookings - Getting bookings for user {userId}");
            
            var bookings = await _bookingHandler.GetByUserIdAsync(userId);
            var response = bookings.Select(b => MapToResponse(b));

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting user bookings", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Get upcoming bookings for current user
    /// </summary>
    [HttpGet("my-bookings/upcoming")]
    public async Task<IActionResult> GetUpcomingBookings()
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"GET /api/bookings/my-bookings/upcoming - Getting upcoming bookings for user {userId}");
            
            var bookings = await _bookingHandler.GetUpcomingByUserIdAsync(userId);
            var response = bookings.Select(b => MapToResponse(b));

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting upcoming bookings", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Get past bookings for current user
    /// </summary>
    [HttpGet("my-bookings/history")]
    public async Task<IActionResult> GetPastBookings()
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"GET /api/bookings/my-bookings/history - Getting past bookings for user {userId}");
            
            var bookings = await _bookingHandler.GetPastByUserIdAsync(userId);
            var response = bookings.Select(b => MapToResponse(b));

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting past bookings", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Get bookings for a company
    /// </summary>
    [HttpGet("company/{companyId:guid}")]
    public async Task<IActionResult> GetCompanyBookings(Guid companyId)
    {
        try
        {
            _logger.LogInfo($"GET /api/bookings/company/{companyId}");
            
            var bookings = await _bookingHandler.GetByCompanyIdAsync(companyId);
            var response = bookings.Select(b => MapToResponse(b));

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting bookings for company {companyId}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Get booking by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            _logger.LogInfo($"GET /api/bookings/{id}");
            
            var booking = await _bookingHandler.GetByIdAsync(id);
            if (booking == null)
                return NotFound();

            var response = MapToResponse(booking);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting booking {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Create a new booking
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookingRequest request)
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"POST /api/bookings - Creating booking for user {userId}");

            var booking = new Booking
            {
                VehicleId = request.VehicleId,
                ServiceOptionId = request.ServiceOptionId,
                AddressId = request.AddressId,
                StartDateTime = request.StartDateTime,
                Notes = request.Notes
            };

            var created = await _bookingHandler.CreateAsync(booking, userId);

            var response = MapToResponse(created);
            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Booking creation failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error creating booking", ex);
            return StatusCode(500, "An error occurred while creating the booking");
        }
    }

    /// <summary>
    /// Update a booking
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBookingRequest request)
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"PUT /api/bookings/{id}");

            var booking = new Booking
            {
                StartDateTime = request.StartDateTime ?? DateTime.UtcNow,
                Notes = request.Notes
            };

            var updated = await _bookingHandler.UpdateAsync(id, booking, userId);
            if (updated == null)
                return NotFound();

            var response = MapToResponse(updated);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Booking update failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating booking {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Cancel a booking
    /// </summary>
    [HttpPut("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        try
        {
            // TODO: Get userId from authentication
            var userId = Guid.NewGuid();
            
            _logger.LogInfo($"PUT /api/bookings/{id}/cancel");

            var result = await _bookingHandler.CancelAsync(id, userId);
            if (!result)
                return NotFound();

            return Ok(new { Message = "Booking cancelled successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Booking cancellation failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error cancelling booking {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Confirm a booking (company action)
    /// </summary>
    [HttpPut("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id)
    {
        try
        {
            // TODO: Get companyId from authentication
            var companyId = Guid.NewGuid();
            
            _logger.LogInfo($"PUT /api/bookings/{id}/confirm by company {companyId}");

            var result = await _bookingHandler.ConfirmAsync(id, companyId);
            if (!result)
                return NotFound();

            return Ok(new { Message = "Booking confirmed successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Booking confirmation failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error confirming booking {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Start service (company action)
    /// </summary>
    [HttpPut("{id:guid}/start")]
    public async Task<IActionResult> StartService(Guid id)
    {
        try
        {
            // TODO: Get companyId from authentication
            var companyId = Guid.NewGuid();
            
            _logger.LogInfo($"PUT /api/bookings/{id}/start by company {companyId}");

            var result = await _bookingHandler.StartServiceAsync(id, companyId);
            if (!result)
                return NotFound();

            return Ok(new { Message = "Service started successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Starting service failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error starting service for booking {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Complete service (company action)
    /// </summary>
    [HttpPut("{id:guid}/complete")]
    public async Task<IActionResult> CompleteService(Guid id)
    {
        try
        {
            // TODO: Get companyId from authentication
            var companyId = Guid.NewGuid();
            
            _logger.LogInfo($"PUT /api/bookings/{id}/complete by company {companyId}");

            var result = await _bookingHandler.CompleteServiceAsync(id, companyId);
            if (!result)
                return NotFound();

            return Ok(new { Message = "Service completed successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Completing service failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error completing service for booking {id}", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    /// <summary>
    /// Check if a time slot is available
    /// </summary>
    [HttpGet("check-availability")]
    public async Task<IActionResult> CheckAvailability(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        try
        {
            _logger.LogInfo($"GET /api/bookings/check-availability - Company: {companyId}, Time: {startTime} to {endTime}");

            var isAvailable = await _bookingHandler.IsTimeSlotAvailableAsync(companyId, startTime, endTime);

            return Ok(new
            {
                IsAvailable = isAvailable,
                CompanyId = companyId,
                StartTime = startTime,
                EndTime = endTime
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error checking availability", ex);
            return StatusCode(500, "An error occurred");
        }
    }

    private static BookingResponse MapToResponse(Booking booking)
    {
        return new BookingResponse
        {
            Id = booking.Id,
            UserId = booking.UserId,
            CompanyId = booking.CompanyId,
            ServiceOptionId = booking.ServiceOptionId,
            VehicleId = booking.VehicleId,
            AddressId = booking.AddressId,
            StartDateTime = booking.StartDateTime,
            EndDateTime = booking.EndDateTime,
            Status = booking.Status,
            Notes = booking.Notes,
            IsPaid = booking.IsPaid,
            CompanyName = booking.Company?.Name ?? string.Empty,
            ServiceName = booking.ServiceOption?.Name ?? string.Empty,
            Price = booking.ServiceOption?.Price ?? 0,
            VehicleInfo = booking.Vehicle != null ? $"{booking.Vehicle.Year} {booking.Vehicle.Make} {booking.Vehicle.Model}".Trim() : string.Empty,
            AddressInfo = booking.Address?.AddressLine ?? string.Empty
        };
    }
}
