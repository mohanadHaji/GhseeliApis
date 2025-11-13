using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Repositories;

/// <summary>
/// Repository implementation for User entity database operations
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;

    public UserRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Gets all users from the database
    /// </summary>
    public async Task<List<User>> GetAllAsync()
    {
        return await _context.Users.ToListAsync();
    }

    /// <summary>
    /// Gets a user by ID from the database
    /// </summary>
    public async Task<User?> GetByIdAsync(Guid id)
    {
        return await _context.Users.FindAsync(id);
    }

    /// <summary>
    /// Adds a new user to the database
    /// </summary>
    public async Task<User> AddAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Updates an existing user in the database
    /// </summary>
    public async Task<User> UpdateAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Deletes a user from the database
    /// </summary>
    public async Task DeleteAsync(User user)
    {
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Checks if a user exists by ID
    /// </summary>
    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _context.Users.AnyAsync(u => u.Id == id);
    }

    /// <summary>
    /// Gets a user by email
    /// </summary>
    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    /// <summary>
    /// Gets a user by username
    /// </summary>
    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.UserName == username);
    }
}
