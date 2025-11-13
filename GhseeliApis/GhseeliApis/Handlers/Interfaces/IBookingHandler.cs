using GhseeliApis.Models;
using GhseeliApis.Models.Enums;

namespace GhseeliApis.Handlers.Interfaces;

/// <summary>
/// Interface for booking-related business logic
/// </summary>
public interface IBookingHandler
{
    /// <summary>
    /// Gets all bookings (admin)
    /// </summary>
    Task<List<Booking>> GetAllAsync();

    /// <summary>
    /// Gets bookings for a specific user
    /// </summary>
    Task<List<Booking>> GetByUserIdAsync(Guid userId);

    /// <summary>
    /// Gets bookings for a specific company
    /// </summary>
    Task<List<Booking>> GetByCompanyIdAsync(Guid companyId);

    /// <summary>
    /// Gets upcoming bookings for a user
    /// </summary>
    Task<List<Booking>> GetUpcomingByUserIdAsync(Guid userId);

    /// <summary>
    /// Gets past bookings for a user
    /// </summary>
    Task<List<Booking>> GetPastByUserIdAsync(Guid userId);

    /// <summary>
    /// Gets a booking by ID with full details
    /// </summary>
    Task<Booking?> GetByIdAsync(Guid id);

    /// <summary>
    /// Creates a new booking with validation and business logic
    /// </summary>
    /// <param name="booking">Booking to create</param>
    /// <param name="userId">User creating the booking</param>
    /// <returns>Created booking</returns>
    Task<Booking> CreateAsync(Booking booking, Guid userId);

    /// <summary>
    /// Updates a booking
    /// </summary>
    Task<Booking?> UpdateAsync(Guid id, Booking booking, Guid userId);

    /// <summary>
    /// Cancels a booking
    /// </summary>
    Task<bool> CancelAsync(Guid id, Guid userId);

    /// <summary>
    /// Confirms a booking (company action)
    /// </summary>
    Task<bool> ConfirmAsync(Guid id, Guid companyId);

    /// <summary>
    /// Starts service (company action)
    /// </summary>
    Task<bool> StartServiceAsync(Guid id, Guid companyId);

    /// <summary>
    /// Completes service (company action)
    /// </summary>
    Task<bool> CompleteServiceAsync(Guid id, Guid companyId);

    /// <summary>
    /// Checks if a time slot is available
    /// </summary>
    Task<bool> IsTimeSlotAvailableAsync(Guid companyId, DateTime startTime, DateTime endTime, Guid? excludeBookingId = null);
}
