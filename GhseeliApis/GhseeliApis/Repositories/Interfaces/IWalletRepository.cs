using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

public interface IWalletRepository
{
    Task<Wallet?> GetByUserIdAsync(Guid userId);
    Task<Wallet> AddAsync(Wallet wallet);
    Task<Wallet> UpdateAsync(Wallet wallet);
    Task<bool> ExistsAsync(Guid userId);
}

public interface IWalletTransactionRepository
{
    Task<List<WalletTransaction>> GetByUserIdAsync(Guid userId);
    Task<WalletTransaction?> GetByIdAsync(Guid id);
    Task<WalletTransaction> AddAsync(WalletTransaction transaction);
}

public interface INotificationRepository
{
    Task<List<Notification>> GetByUserIdAsync(Guid userId);
    Task<List<Notification>> GetUnreadByUserIdAsync(Guid userId);
    Task<Notification?> GetByIdAsync(Guid id);
    Task<Notification> AddAsync(Notification notification);
    Task<Notification> UpdateAsync(Notification notification);
    Task DeleteAsync(Notification notification);
    Task MarkAsReadAsync(Guid id);
    Task MarkAllAsReadAsync(Guid userId);
}

public interface ICompanyAvailabilityRepository
{
    Task<List<CompanyAvailability>> GetByCompanyIdAsync(Guid companyId);
    Task<List<CompanyAvailability>> GetByCompanyIdAndDateAsync(Guid companyId, DateTime date);
    Task<CompanyAvailability?> GetByIdAsync(Guid id);
    Task<CompanyAvailability> AddAsync(CompanyAvailability availability);
    Task<CompanyAvailability> UpdateAsync(CompanyAvailability availability);
    Task DeleteAsync(CompanyAvailability availability);
}
