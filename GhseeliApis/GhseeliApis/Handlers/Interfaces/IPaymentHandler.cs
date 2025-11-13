namespace GhseeliApis.Handlers.Interfaces;

/// <summary>
/// Interface for payment-related business logic
/// </summary>
public interface IPaymentHandler
{
    /// <summary>
    /// Gets all payments
    /// </summary>
    Task<List<Models.Payment>> GetAllAsync();
    
    /// <summary>
    /// Gets a payment by ID
    /// </summary>
    Task<Models.Payment?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Gets payments for a specific user
    /// </summary>
    Task<List<Models.Payment>> GetByUserIdAsync(Guid userId);
    
    /// <summary>
    /// Gets payment for a specific booking
    /// </summary>
    Task<Models.Payment?> GetByBookingIdAsync(Guid bookingId);
    
    /// <summary>
    /// Creates a new payment
    /// </summary>
    Task<Models.Payment> CreateAsync(Models.Payment payment, Guid userId);
    
    /// <summary>
    /// Updates payment status (e.g., mark as completed, failed)
    /// </summary>
    Task<Models.Payment?> UpdateStatusAsync(Guid id, Models.Enums.PaymentStatus status);
    
    /// <summary>
    /// Processes a refund for a payment
    /// </summary>
    Task<Models.Payment?> ProcessRefundAsync(Guid id, Guid userId);
}
