using GhseeliApis.Models;
using GhseeliApis.Models.Enums;

namespace GhseeliApis.Repositories.Interfaces;

/// <summary>
/// Repository interface for Booking entity database operations
/// </summary>
public interface IBookingRepository
{
    Task<List<Booking>> GetAllAsync();
    Task<List<Booking>> GetByUserIdAsync(Guid userId);
    Task<List<Booking>> GetByCompanyIdAsync(Guid companyId);
    Task<List<Booking>> GetUpcomingByUserIdAsync(Guid userId);
    Task<List<Booking>> GetPastByUserIdAsync(Guid userId);
    Task<List<Booking>> GetByStatusAsync(BookingStatus status);
    Task<Booking?> GetByIdAsync(Guid id);
    Task<Booking?> GetByIdWithDetailsAsync(Guid id);
    Task<Booking> AddAsync(Booking booking);
    Task<Booking> UpdateAsync(Booking booking);
    Task DeleteAsync(Booking booking);
    Task<bool> ExistsAsync(Guid id);
    Task<bool> HasConflictAsync(Guid companyId, DateTime startTime, DateTime endTime, Guid? excludeBookingId = null);
}
