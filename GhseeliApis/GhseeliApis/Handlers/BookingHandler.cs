using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Handlers;

/// <summary>
/// Handler for booking-related business logic
/// </summary>
public class BookingHandler : IBookingHandler
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IServiceOptionRepository _serviceOptionRepository;
    private readonly IVehicleRepository _vehicleRepository;
    private readonly IUserAddressRepository _addressRepository;
    private readonly IAppLogger _logger;

    public BookingHandler(
        IBookingRepository bookingRepository,
        IServiceOptionRepository serviceOptionRepository,
        IVehicleRepository vehicleRepository,
        IUserAddressRepository addressRepository,
        IAppLogger logger)
    {
        _bookingRepository = bookingRepository;
        _serviceOptionRepository = serviceOptionRepository;
        _vehicleRepository = vehicleRepository;
        _addressRepository = addressRepository;
        _logger = logger;
    }

    public async Task<List<Booking>> GetAllAsync()
    {
        try
        {
            _logger.LogInfo("BookingHandler: Getting all bookings");
            return await _bookingRepository.GetAllAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("BookingHandler: Error getting all bookings", ex);
            throw;
        }
    }

    public async Task<List<Booking>> GetByUserIdAsync(Guid userId)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Getting bookings for user {userId}");
            return await _bookingRepository.GetByUserIdAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BookingHandler: Error getting bookings for user {userId}", ex);
            throw;
        }
    }

    public async Task<List<Booking>> GetByCompanyIdAsync(Guid companyId)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Getting bookings for company {companyId}");
            return await _bookingRepository.GetByCompanyIdAsync(companyId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BookingHandler: Error getting bookings for company {companyId}", ex);
            throw;
        }
    }

    public async Task<List<Booking>> GetUpcomingByUserIdAsync(Guid userId)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Getting upcoming bookings for user {userId}");
            return await _bookingRepository.GetUpcomingByUserIdAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BookingHandler: Error getting upcoming bookings for user {userId}", ex);
            throw;
        }
    }

    public async Task<List<Booking>> GetPastByUserIdAsync(Guid userId)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Getting past bookings for user {userId}");
            return await _bookingRepository.GetPastByUserIdAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BookingHandler: Error getting past bookings for user {userId}", ex);
            throw;
        }
    }

    public async Task<Booking?> GetByIdAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Getting booking {id}");
            return await _bookingRepository.GetByIdWithDetailsAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BookingHandler: Error getting booking {id}", ex);
            throw;
        }
    }

    public async Task<Booking> CreateAsync(Booking booking, Guid userId)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Creating booking for user {userId}");

            // Set user ID
            booking.UserId = userId;
            booking.Id = Guid.NewGuid();

            // Validate vehicle belongs to user
            var vehicle = await _vehicleRepository.GetByIdAsync(booking.VehicleId);
            if (vehicle == null || vehicle.UserId != userId)
            {
                _logger.LogWarning($"BookingHandler: Vehicle {booking.VehicleId} not found or doesn't belong to user {userId}");
                throw new InvalidOperationException("Vehicle not found or doesn't belong to you");
            }

            // Validate address belongs to user
            var address = await _addressRepository.GetByIdAsync(booking.AddressId);
            if (address == null || address.UserId != userId)
            {
                _logger.LogWarning($"BookingHandler: Address {booking.AddressId} not found or doesn't belong to user {userId}");
                throw new InvalidOperationException("Address not found or doesn't belong to you");
            }

            // Get service option to calculate end time and get company
            var serviceOption = await _serviceOptionRepository.GetByIdAsync(booking.ServiceOptionId);
            if (serviceOption == null)
            {
                _logger.LogWarning($"BookingHandler: Service option {booking.ServiceOptionId} not found");
                throw new InvalidOperationException("Service option not found");
            }

            // Set company ID from service option
            booking.CompanyId = serviceOption.CompanyId ?? throw new InvalidOperationException("Service option must have a company");

            // Calculate end time based on service duration
            booking.EndDateTime = booking.StartDateTime.AddMinutes(serviceOption.DurationMinutes);

            _logger.LogInfo($"BookingHandler: Booking end time calculated as {booking.EndDateTime} (duration: {serviceOption.DurationMinutes} minutes)");

            // Check for time slot conflicts
            var hasConflict = await _bookingRepository.HasConflictAsync(
                booking.CompanyId,
                booking.StartDateTime,
                booking.EndDateTime);

            if (hasConflict)
            {
                _logger.LogWarning($"BookingHandler: Time slot conflict for company {booking.CompanyId} at {booking.StartDateTime}");
                throw new InvalidOperationException("The selected time slot is not available. Please choose a different time.");
            }

            // Set initial status
            booking.Status = BookingStatus.Pending;
            booking.IsPaid = false;

            // Validate the booking
            var validationResult = booking.Validate();
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"BookingHandler: Booking validation failed: {string.Join(", ", validationResult.Errors)}");
                throw new InvalidOperationException($"Booking validation failed: {string.Join(", ", validationResult.Errors)}");
            }

            var created = await _bookingRepository.AddAsync(booking);
            
            _logger.LogInfo($"BookingHandler: Booking {created.Id} created successfully for user {userId}");
            
            return created;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError("BookingHandler: Database error creating booking", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("BookingHandler: Error creating booking", ex);
            throw;
        }
    }

    public async Task<Booking?> UpdateAsync(Guid id, Booking updatedBooking, Guid userId)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Updating booking {id}");

            var existing = await _bookingRepository.GetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning($"BookingHandler: Booking {id} not found");
                return null;
            }

            // Ensure booking belongs to user
            if (existing.UserId != userId)
            {
                _logger.LogWarning($"BookingHandler: User {userId} does not own booking {id}");
                return null;
            }

            // Only allow updates to pending or confirmed bookings
            if (existing.Status != BookingStatus.Pending && existing.Status != BookingStatus.Confirmed)
            {
                _logger.LogWarning($"BookingHandler: Cannot update booking {id} with status {existing.Status}");
                throw new InvalidOperationException($"Cannot update booking with status {existing.Status}");
            }

            // Update allowed fields
            existing.StartDateTime = updatedBooking.StartDateTime;
            existing.Notes = updatedBooking.Notes;

            // Recalculate end time if start time changed
            var serviceOption = await _serviceOptionRepository.GetByIdAsync(existing.ServiceOptionId);
            if (serviceOption != null)
            {
                existing.EndDateTime = existing.StartDateTime.AddMinutes(serviceOption.DurationMinutes);
            }

            // Check for conflicts with new time
            var hasConflict = await _bookingRepository.HasConflictAsync(
                existing.CompanyId,
                existing.StartDateTime,
                existing.EndDateTime,
                id); // Exclude this booking from conflict check

            if (hasConflict)
            {
                _logger.LogWarning($"BookingHandler: Time slot conflict when updating booking {id}");
                throw new InvalidOperationException("The updated time slot is not available. Please choose a different time.");
            }

            var updated = await _bookingRepository.UpdateAsync(existing);
            
            _logger.LogInfo($"BookingHandler: Booking {id} updated successfully");
            
            return updated;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError($"BookingHandler: Database error updating booking {id}", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"BookingHandler: Error updating booking {id}", ex);
            throw;
        }
    }

    public async Task<bool> CancelAsync(Guid id, Guid userId)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Cancelling booking {id} by user {userId}");

            var booking = await _bookingRepository.GetByIdAsync(id);
            if (booking == null)
            {
                _logger.LogWarning($"BookingHandler: Booking {id} not found");
                return false;
            }

            // Ensure booking belongs to user
            if (booking.UserId != userId)
            {
                _logger.LogWarning($"BookingHandler: User {userId} does not own booking {id}");
                return false;
            }

            // Check if booking can be cancelled
            if (booking.Status == BookingStatus.Completed || booking.Status == BookingStatus.Cancelled)
            {
                _logger.LogWarning($"BookingHandler: Cannot cancel booking {id} with status {booking.Status}");
                throw new InvalidOperationException($"Cannot cancel booking with status {booking.Status}");
            }

            booking.Status = BookingStatus.Cancelled;
            await _bookingRepository.UpdateAsync(booking);

            _logger.LogInfo($"BookingHandler: Booking {id} cancelled successfully");
            
            return true;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"BookingHandler: Error cancelling booking {id}", ex);
            throw;
        }
    }

    public async Task<bool> ConfirmAsync(Guid id, Guid companyId)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Confirming booking {id} by company {companyId}");

            var booking = await _bookingRepository.GetByIdAsync(id);
            if (booking == null)
            {
                _logger.LogWarning($"BookingHandler: Booking {id} not found");
                return false;
            }

            // Ensure booking belongs to company
            if (booking.CompanyId != companyId)
            {
                _logger.LogWarning($"BookingHandler: Company {companyId} does not own booking {id}");
                return false;
            }

            // Only confirm pending bookings
            if (booking.Status != BookingStatus.Pending)
            {
                _logger.LogWarning($"BookingHandler: Cannot confirm booking {id} with status {booking.Status}");
                throw new InvalidOperationException("Only pending bookings can be confirmed");
            }

            booking.Status = BookingStatus.Confirmed;
            await _bookingRepository.UpdateAsync(booking);

            _logger.LogInfo($"BookingHandler: Booking {id} confirmed successfully");
            
            return true;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"BookingHandler: Error confirming booking {id}", ex);
            throw;
        }
    }

    public async Task<bool> StartServiceAsync(Guid id, Guid companyId)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Starting service for booking {id} by company {companyId}");

            var booking = await _bookingRepository.GetByIdAsync(id);
            if (booking == null || booking.CompanyId != companyId)
            {
                _logger.LogWarning($"BookingHandler: Booking {id} not found or doesn't belong to company {companyId}");
                return false;
            }

            if (booking.Status != BookingStatus.Confirmed)
            {
                _logger.LogWarning($"BookingHandler: Cannot start service for booking {id} with status {booking.Status}");
                throw new InvalidOperationException("Only confirmed bookings can be started");
            }

            booking.Status = BookingStatus.InProgress;
            await _bookingRepository.UpdateAsync(booking);

            _logger.LogInfo($"BookingHandler: Service started for booking {id}");
            
            return true;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"BookingHandler: Error starting service for booking {id}", ex);
            throw;
        }
    }

    public async Task<bool> CompleteServiceAsync(Guid id, Guid companyId)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Completing service for booking {id} by company {companyId}");

            var booking = await _bookingRepository.GetByIdAsync(id);
            if (booking == null || booking.CompanyId != companyId)
            {
                _logger.LogWarning($"BookingHandler: Booking {id} not found or doesn't belong to company {companyId}");
                return false;
            }

            if (booking.Status != BookingStatus.InProgress)
            {
                _logger.LogWarning($"BookingHandler: Cannot complete booking {id} with status {booking.Status}");
                throw new InvalidOperationException("Only in-progress bookings can be completed");
            }

            booking.Status = BookingStatus.Completed;
            await _bookingRepository.UpdateAsync(booking);

            _logger.LogInfo($"BookingHandler: Service completed for booking {id}");
            
            return true;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"BookingHandler: Error completing service for booking {id}", ex);
            throw;
        }
    }

    public async Task<bool> IsTimeSlotAvailableAsync(Guid companyId, DateTime startTime, DateTime endTime, Guid? excludeBookingId = null)
    {
        try
        {
            _logger.LogInfo($"BookingHandler: Checking time slot availability for company {companyId} from {startTime} to {endTime}");
            
            var hasConflict = await _bookingRepository.HasConflictAsync(companyId, startTime, endTime, excludeBookingId);
            
            return !hasConflict;
        }
        catch (Exception ex)
        {
            _logger.LogError($"BookingHandler: Error checking time slot availability", ex);
            throw;
        }
    }
}
