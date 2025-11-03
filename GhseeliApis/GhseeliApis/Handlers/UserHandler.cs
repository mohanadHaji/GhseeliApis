using GhseeliApis.Data;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Handlers;

/// <summary>
/// Handler for user-related database operations
/// </summary>
public class UserHandler : IUserHandler
{
    private readonly ApplicationDbContext _db;
    private readonly IAppLogger _logger;

    public UserHandler(ApplicationDbContext db, IAppLogger logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Gets all users
    /// </summary>
    public async Task<List<User>> GetAllUsersAsync()
    {
        try
        {
            _logger.LogInfo("GetAllUsersAsync: Starting to retrieve all users from database");
            
            var users = await _db.Users.ToListAsync();
            
            _logger.LogInfo($"GetAllUsersAsync: Successfully retrieved {users.Count} user(s)");
            
            if (users.Count == 0)
            {
                _logger.LogWarning("GetAllUsersAsync: No users found in database");
            }
            
            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError("GetAllUsersAsync: Failed to retrieve users from database", ex);
            throw;
        }
    }

    /// <summary>
    /// Gets a user by ID
    /// </summary>
    public async Task<User?> GetUserByIdAsync(int id)
    {
        try
        {
            _logger.LogInfo($"GetUserByIdAsync: Attempting to retrieve user with ID={id}");
            
            var user = await _db.Users.FindAsync(id);
            
            if (user == null)
            {
                _logger.LogWarning($"GetUserByIdAsync: User with ID={id} not found in database");
                return null;
            }
            
            _logger.LogInfo($"GetUserByIdAsync: Successfully retrieved user ID={id}, Name='{user.Name}', Email='{user.Email}', IsActive={user.IsActive}");
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError($"GetUserByIdAsync: Database error while retrieving user with ID={id}", ex);
            throw;
        }
    }

    /// <summary>
    /// Creates a new user
    /// </summary>
    public async Task<User> CreateUserAsync(User user)
    {
        try
        {
            _logger.LogInfo($"CreateUserAsync: Starting user creation - Name='{user.Name}', Email='{user.Email}', IsActive={user.IsActive}");
            
            _db.Users.Add(user);
            
            _logger.LogInfo("CreateUserAsync: User entity added to context, saving changes...");
            await _db.SaveChangesAsync();
            
            _logger.LogInfo($"CreateUserAsync: User created successfully - ID={user.Id}, Name='{user.Name}', CreatedAt={user.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            
            return user;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError($"CreateUserAsync: Database update failed for user Name='{user.Name}', Email='{user.Email}' - Possible duplicate or constraint violation", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"CreateUserAsync: Unexpected error while creating user Name='{user.Name}', Email='{user.Email}'", ex);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing user
    /// </summary>
    public async Task<User?> UpdateUserAsync(int id, User updatedUser)
    {
        try
        {
            _logger.LogInfo($"UpdateUserAsync: Starting update for user ID={id} with new data - Name='{updatedUser.Name}', Email='{updatedUser.Email}', IsActive={updatedUser.IsActive}");
            
            var user = await _db.Users.FindAsync(id);
            
            if (user is null)
            {
                _logger.LogWarning($"UpdateUserAsync: Cannot update - User with ID={id} not found in database");
                return null;
            }

            var oldName = user.Name;
            var oldEmail = user.Email;
            var oldIsActive = user.IsActive;

            user.Name = updatedUser.Name;
            user.Email = updatedUser.Email;
            user.IsActive = updatedUser.IsActive;
            user.UpdatedAt = DateTime.UtcNow;

            _logger.LogInfo($"UpdateUserAsync: Saving changes for user ID={id} - Changed: Name '{oldName}'=>'{user.Name}', Email '{oldEmail}'=>'{user.Email}', IsActive {oldIsActive}=>{user.IsActive}");
            
            await _db.SaveChangesAsync();
            
            _logger.LogInfo($"UpdateUserAsync: User ID={id} updated successfully at {user.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
            
            return user;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError($"UpdateUserAsync: Concurrency conflict while updating user ID={id} - User may have been modified by another process", ex);
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError($"UpdateUserAsync: Database update failed for user ID={id} - Possible constraint violation", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"UpdateUserAsync: Unexpected error while updating user ID={id}", ex);
            throw;
        }
    }

    /// <summary>
    /// Deletes a user by ID
    /// </summary>
    public async Task<bool> DeleteUserAsync(int id)
    {
        try
        {
            _logger.LogInfo($"DeleteUserAsync: Attempting to delete user with ID={id}");
            
            var user = await _db.Users.FindAsync(id);
            
            if (user is null)
            {
                _logger.LogWarning($"DeleteUserAsync: Cannot delete - User with ID={id} not found in database");
                return false;
            }

            var userName = user.Name;
            var userEmail = user.Email;

            _db.Users.Remove(user);
            
            _logger.LogInfo($"DeleteUserAsync: Removing user ID={id}, Name='{userName}', Email='{userEmail}' from database...");
            
            await _db.SaveChangesAsync();
            
            _logger.LogInfo($"DeleteUserAsync: User ID={id} ('{userName}') deleted successfully from database");
            
            return true;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError($"DeleteUserAsync: Database error while deleting user ID={id} - May have foreign key constraints", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"DeleteUserAsync: Unexpected error while deleting user ID={id}", ex);
            throw;
        }
    }
}
