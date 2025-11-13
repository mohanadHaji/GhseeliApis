using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

public class WalletRepository : IWalletRepository
{
    private readonly ApplicationDbContext _context;

    public WalletRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Wallet?> GetByUserIdAsync(Guid userId)
    {
        return await _context.Wallets
            .Include(w => w.Transactions)
            .FirstOrDefaultAsync(w => w.UserId == userId);
    }

    public async Task<Wallet> AddAsync(Wallet wallet)
    {
        _context.Wallets.Add(wallet);
        await _context.SaveChangesAsync();
        return wallet;
    }

    public async Task<Wallet> UpdateAsync(Wallet wallet)
    {
        _context.Wallets.Update(wallet);
        await _context.SaveChangesAsync();
        return wallet;
    }

    public async Task<bool> ExistsAsync(Guid userId)
    {
        return await _context.Wallets.AnyAsync(w => w.UserId == userId);
    }
}

public class WalletTransactionRepository : IWalletTransactionRepository
{
    private readonly ApplicationDbContext _context;

    public WalletTransactionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<WalletTransaction>> GetByUserIdAsync(Guid userId)
    {
        return await _context.WalletTransactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<WalletTransaction?> GetByIdAsync(Guid id)
    {
        return await _context.WalletTransactions.FindAsync(id);
    }

    public async Task<WalletTransaction> AddAsync(WalletTransaction transaction)
    {
        _context.WalletTransactions.Add(transaction);
        await _context.SaveChangesAsync();
        return transaction;
    }
}

public class NotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _context;

    public NotificationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Notification>> GetByUserIdAsync(Guid userId)
    {
        return await _context.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Notification>> GetUnreadByUserIdAsync(Guid userId)
    {
        return await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();
    }

    public async Task<Notification?> GetByIdAsync(Guid id)
    {
        return await _context.Notifications.FindAsync(id);
    }

    public async Task<Notification> AddAsync(Notification notification)
    {
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();
        return notification;
    }

    public async Task<Notification> UpdateAsync(Notification notification)
    {
        _context.Notifications.Update(notification);
        await _context.SaveChangesAsync();
        return notification;
    }

    public async Task DeleteAsync(Notification notification)
    {
        _context.Notifications.Remove(notification);
        await _context.SaveChangesAsync();
    }

    public async Task MarkAsReadAsync(Guid id)
    {
        var notification = await _context.Notifications.FindAsync(id);
        if (notification != null)
        {
            notification.IsRead = true;
            await _context.SaveChangesAsync();
        }
    }

    public async Task MarkAllAsReadAsync(Guid userId)
    {
        var notifications = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
        }

        await _context.SaveChangesAsync();
    }
}

public class CompanyAvailabilityRepository : ICompanyAvailabilityRepository
{
    private readonly ApplicationDbContext _context;

    public CompanyAvailabilityRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<CompanyAvailability>> GetByCompanyIdAsync(Guid companyId)
    {
        return await _context.CompanyAvailabilities
            .Where(a => a.CompanyId == companyId)
            .ToListAsync();
    }

    public async Task<List<CompanyAvailability>> GetByCompanyIdAndDateAsync(Guid companyId, DateTime date)
    {
        var dayOfWeek = date.DayOfWeek;
        return await _context.CompanyAvailabilities
            .Where(a => a.CompanyId == companyId &&
                       ((a.DayOfWeek == dayOfWeek && a.SpecificDate == null) ||
                        (a.SpecificDate != null && a.SpecificDate.Value.Date == date.Date)))
            .ToListAsync();
    }

    public async Task<CompanyAvailability?> GetByIdAsync(Guid id)
    {
        return await _context.CompanyAvailabilities.FindAsync(id);
    }

    public async Task<CompanyAvailability> AddAsync(CompanyAvailability availability)
    {
        _context.CompanyAvailabilities.Add(availability);
        await _context.SaveChangesAsync();
        return availability;
    }

    public async Task<CompanyAvailability> UpdateAsync(CompanyAvailability availability)
    {
        _context.CompanyAvailabilities.Update(availability);
        await _context.SaveChangesAsync();
        return availability;
    }

    public async Task DeleteAsync(CompanyAvailability availability)
    {
        _context.CompanyAvailabilities.Remove(availability);
        await _context.SaveChangesAsync();
    }
}
