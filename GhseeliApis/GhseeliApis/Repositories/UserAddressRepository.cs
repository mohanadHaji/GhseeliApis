using GhseeliApis.Models;
using GhseeliApis.Models.Enums;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

/// <summary>
/// Repository implementation for UserAddress entity database operations
/// </summary>
public class UserAddressRepository : IUserAddressRepository
{
    private readonly ApplicationDbContext _context;

    public UserAddressRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<UserAddress>> GetAllAsync()
    {
        return await _context.UserAddresses
            .Include(a => a.User)
            .ToListAsync();
    }

    public async Task<List<UserAddress>> GetByUserIdAsync(Guid userId)
    {
        return await _context.UserAddresses
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsPrimary)
            .ToListAsync();
    }

    public async Task<UserAddress?> GetByIdAsync(Guid id)
    {
        return await _context.UserAddresses
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<UserAddress?> GetPrimaryAddressAsync(Guid userId)
    {
        return await _context.UserAddresses
            .FirstOrDefaultAsync(a => a.UserId == userId && a.IsPrimary);
    }

    public async Task<UserAddress> AddAsync(UserAddress address)
    {
        // If this is set as primary, unset others
        if (address.IsPrimary)
        {
            await UnsetPrimaryForUserAsync(address.UserId);
        }

        _context.UserAddresses.Add(address);
        await _context.SaveChangesAsync();
        return address;
    }

    public async Task<UserAddress> UpdateAsync(UserAddress address)
    {
        _context.UserAddresses.Update(address);
        await _context.SaveChangesAsync();
        return address;
    }

    public async Task DeleteAsync(UserAddress address)
    {
        _context.UserAddresses.Remove(address);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.UserAddresses.AnyAsync(a => a.Id == id);
    }

    public async Task SetAsPrimaryAsync(Guid addressId, Guid userId)
    {
        // Unset all primary addresses for this user
        await UnsetPrimaryForUserAsync(userId);

        // Set this address as primary
        var address = await _context.UserAddresses.FindAsync(addressId);
        if (address != null && address.UserId == userId)
        {
            address.IsPrimary = true;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> HasActiveBookingsAsync(Guid addressId)
    {
        return await _context.Bookings
            .AnyAsync(b => b.AddressId == addressId &&
                          (b.Status == BookingStatus.Pending ||
                           b.Status == BookingStatus.Confirmed ||
                           b.Status == BookingStatus.InProgress));
    }

    private async Task UnsetPrimaryForUserAsync(Guid userId)
    {
        var primaryAddresses = await _context.UserAddresses
            .Where(a => a.UserId == userId && a.IsPrimary)
            .ToListAsync();

        foreach (var address in primaryAddresses)
        {
            address.IsPrimary = false;
        }

        if (primaryAddresses.Any())
        {
            await _context.SaveChangesAsync();
        }
    }
}
