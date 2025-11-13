using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Handlers;

/// <summary>
/// Handler for user-related business logic
/// </summary>
public class UserHandler : IUserHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IAppLogger _logger;

    public UserHandler(IUserRepository userRepository, IAppLogger logger)
    {
        _userRepository = userRepository;
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
            
            var users = await _userRepository.GetAllAsync();
            
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
    public async Task<User?> GetUserByIdAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"GetUserByIdAsync: Attempting to retrieve user with ID={id}");
            
            var user = await _userRepository.GetByIdAsync(id);
            
            if (user == null)
            {
                _logger.LogWarning($"GetUserByIdAsync: User with ID={id} not found in database");
                return null;
            }
            
            _logger.LogInfo($"GetUserByIdAsync: Successfully retrieved user ID={id}, UserName='{user.UserName}', Email='{user.Email}', IsActive={user.IsActive}");
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
            _logger.LogInfo($"CreateUserAsync: Starting user creation - UserName='{user.UserName}', Email='{user.Email}', IsActive={user.IsActive}");
            
            var createdUser = await _userRepository.AddAsync(user);
            
            _logger.LogInfo($"CreateUserAsync: User created successfully - ID={createdUser.Id}, UserName='{createdUser.UserName}', CreatedAt={createdUser.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            
            return createdUser;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError($"CreateUserAsync: Database update failed for user UserName='{user.UserName}', Email='{user.Email}' - Possible duplicate or constraint violation", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"CreateUserAsync: Unexpected error while creating user UserName='{user.UserName}', Email='{user.Email}'", ex);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing user
    /// </summary>
    public async Task<User?> UpdateUserAsync(Guid id, User updatedUser)
    {
        try
        {
            _logger.LogInfo($"UpdateUserAsync: Starting update for user ID={id} with new data - UserName='{updatedUser.UserName}', Email='{updatedUser.Email}', FullName='{updatedUser.FullName}', IsActive={updatedUser.IsActive}");
            
            var user = await _userRepository.GetByIdAsync(id);
            
            if (user is null)
            {
                _logger.LogWarning($"UpdateUserAsync: Cannot update - User with ID={id} not found in database");
                return null;
            }

            var oldUserName = user.UserName;
            var oldEmail = user.Email;
            var oldFullName = user.FullName;
            var oldIsActive = user.IsActive;

            user.UserName = updatedUser.UserName;
            user.Email = updatedUser.Email;
            user.FullName = updatedUser.FullName;
            user.Phone = updatedUser.Phone;
            user.IsActive = updatedUser.IsActive;
            user.UpdatedAt = DateTime.UtcNow;

            _logger.LogInfo($"UpdateUserAsync: Saving changes for user ID={id} - Changed: UserName '{oldUserName}'=>'{user.UserName}', Email '{oldEmail}'=>'{user.Email}', FullName '{oldFullName}'=>'{user.FullName}', IsActive {oldIsActive}=>{user.IsActive}");
            
            var result = await _userRepository.UpdateAsync(user);
            
            _logger.LogInfo($"UpdateUserAsync: User ID={id} updated successfully at {result.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
            
            return result;
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
    public async Task<bool> DeleteUserAsync(Guid id)
    {
        try
        {
            _logger.LogInfo($"DeleteUserAsync: Attempting to delete user with ID={id}");
            
            var user = await _userRepository.GetByIdAsync(id);
            
            if (user is null)
            {
                _logger.LogWarning($"DeleteUserAsync: Cannot delete - User with ID={id} not found in database");
                return false;
            }

            var userName = user.UserName;
            var userEmail = user.Email;

            _logger.LogInfo($"DeleteUserAsync: Removing user ID={id}, UserName='{userName}', Email='{userEmail}' from database...");
            
            await _userRepository.DeleteAsync(user);
            
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
