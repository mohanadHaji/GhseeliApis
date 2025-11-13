using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly ApplicationDbContext _context;

    public PaymentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Payment>> GetAllAsync()
    {
        return await _context.Payments
            .Include(p => p.User)
            .Include(p => p.Booking)
            .ToListAsync();
    }

    public async Task<List<Payment>> GetByUserIdAsync(Guid userId)
    {
        return await _context.Payments
            .Where(p => p.UserId == userId)
            .Include(p => p.Booking)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<Payment?> GetByIdAsync(Guid id)
    {
        return await _context.Payments
            .Include(p => p.User)
            .Include(p => p.Booking)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Payment?> GetByBookingIdAsync(Guid bookingId)
    {
        return await _context.Payments
            .FirstOrDefaultAsync(p => p.BookingId == bookingId);
    }

    public async Task<Payment> AddAsync(Payment payment)
    {
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();
        return payment;
    }

    public async Task<Payment> UpdateAsync(Payment payment)
    {
        _context.Payments.Update(payment);
        await _context.SaveChangesAsync();
        return payment;
    }

    public async Task DeleteAsync(Payment payment)
    {
        _context.Payments.Remove(payment);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.Payments.AnyAsync(p => p.Id == id);
    }
}
