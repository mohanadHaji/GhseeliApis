using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

public interface IPaymentRepository
{
    Task<List<Payment>> GetAllAsync();
    Task<List<Payment>> GetByUserIdAsync(Guid userId);
    Task<Payment?> GetByIdAsync(Guid id);
    Task<Payment?> GetByBookingIdAsync(Guid bookingId);
    Task<Payment> AddAsync(Payment payment);
    Task<Payment> UpdateAsync(Payment payment);
    Task DeleteAsync(Payment payment);
    Task<bool> ExistsAsync(Guid id);
}
