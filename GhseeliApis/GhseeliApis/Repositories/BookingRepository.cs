using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

/// <summary>
/// Repository implementation for Booking entity database operations
/// </summary>
public class BookingRepository : IBookingRepository
{
    private readonly ApplicationDbContext _context;

    public BookingRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Booking>> GetAllAsync()
    {
        return await _context.Bookings
            .Include(b => b.User)
            .Include(b => b.Company)
            .Include(b => b.ServiceOption)
            .Include(b => b.Vehicle)
            .Include(b => b.Address)
            .Include(b => b.Payment)
            .ToListAsync();
    }

    public async Task<List<Booking>> GetByUserIdAsync(Guid userId)
    {
        return await _context.Bookings
            .Where(b => b.UserId == userId)
            .Include(b => b.Company)
            .Include(b => b.ServiceOption)
            .Include(b => b.Vehicle)
            .Include(b => b.Address)
            .Include(b => b.Payment)
            .OrderByDescending(b => b.StartDateTime)
            .ToListAsync();
    }

    public async Task<List<Booking>> GetByCompanyIdAsync(Guid companyId)
    {
        return await _context.Bookings
            .Where(b => b.CompanyId == companyId)
            .Include(b => b.User)
            .Include(b => b.ServiceOption)
            .Include(b => b.Vehicle)
            .Include(b => b.Address)
            .Include(b => b.Payment)
            .OrderByDescending(b => b.StartDateTime)
            .ToListAsync();
    }

    public async Task<List<Booking>> GetUpcomingByUserIdAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        return await _context.Bookings
            .Where(b => b.UserId == userId && b.StartDateTime > now)
            .Include(b => b.Company)
            .Include(b => b.ServiceOption)
            .Include(b => b.Vehicle)
            .Include(b => b.Address)
            .OrderBy(b => b.StartDateTime)
            .ToListAsync();
    }

    public async Task<List<Booking>> GetPastByUserIdAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        return await _context.Bookings
            .Where(b => b.UserId == userId && b.EndDateTime < now)
            .Include(b => b.Company)
            .Include(b => b.ServiceOption)
            .Include(b => b.Vehicle)
            .Include(b => b.Address)
            .Include(b => b.Payment)
            .OrderByDescending(b => b.StartDateTime)
            .ToListAsync();
    }

    public async Task<List<Booking>> GetByStatusAsync(BookingStatus status)
    {
        return await _context.Bookings
            .Where(b => b.Status == status)
            .Include(b => b.User)
            .Include(b => b.Company)
            .Include(b => b.ServiceOption)
            .Include(b => b.Vehicle)
            .Include(b => b.Address)
            .ToListAsync();
    }

    public async Task<Booking?> GetByIdAsync(Guid id)
    {
        return await _context.Bookings.FindAsync(id);
    }

    public async Task<Booking?> GetByIdWithDetailsAsync(Guid id)
    {
        return await _context.Bookings
            .Include(b => b.User)
            .Include(b => b.Company)
            .Include(b => b.ServiceOption)
            .Include(b => b.Vehicle)
            .Include(b => b.Address)
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<Booking> AddAsync(Booking booking)
    {
        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();
        return booking;
    }

    public async Task<Booking> UpdateAsync(Booking booking)
    {
        _context.Bookings.Update(booking);
        await _context.SaveChangesAsync();
        return booking;
    }

    public async Task DeleteAsync(Booking booking)
    {
        _context.Bookings.Remove(booking);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.Bookings.AnyAsync(b => b.Id == id);
    }

    public async Task<bool> HasConflictAsync(Guid companyId, DateTime startTime, DateTime endTime, Guid? excludeBookingId = null)
    {
        var query = _context.Bookings
            .Where(b => b.CompanyId == companyId &&
                       (b.Status == BookingStatus.Pending ||
                        b.Status == BookingStatus.Confirmed ||
                        b.Status == BookingStatus.InProgress) &&
                       ((b.StartDateTime < endTime && b.EndDateTime > startTime)));

        if (excludeBookingId.HasValue)
        {
            query = query.Where(b => b.Id != excludeBookingId.Value);
        }

        return await query.AnyAsync();
    }
}
