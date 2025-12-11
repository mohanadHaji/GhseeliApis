using GhseeliApis.DTOs.User;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Handlers;

/// <summary>
/// Handler for user-related business logic
/// </summary>
public class UserHandler : IUserHandler
{
    private readonly IUserRepository _userRepository;
    private readonly UserManager<User> _userManager;
    private readonly IAppLogger _logger;

    public UserHandler(
        IUserRepository userRepository,
        UserManager<User> userManager,
        IAppLogger logger)
    {
        _userRepository = userRepository;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets all users with their roles
    /// </summary>
    public async Task<List<UserListResponse>> GetAllUsersAsync()
    {
        try
        {
            _logger.LogInfo("GetAllUsersAsync: Starting to retrieve all users from database");
            
            var users = await _userRepository.GetAllAsync();
            
            var response = new List<UserListResponse>();
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                response.Add(new UserListResponse
                {
                    Id = user.Id,
                    Email = user.Email!,
                    FullName = user.FullName,
                    Phone = user.Phone,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    Roles = roles.ToList()
                });
            }
            
            _logger.LogInfo($"GetAllUsersAsync: Successfully retrieved {response.Count} user(s)");
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError("GetAllUsersAsync: Failed to retrieve users from database", ex);
            throw;
        }
    }

    /// <summary>
    /// Gets a user by ID with detailed information
    /// </summary>
    public async Task<UserResponse?> GetUserByIdAsync(Guid id)
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

            var roles = await _userManager.GetRolesAsync(user);
            
            var response = new UserResponse
            {
                Id = user.Id,
                Email = user.Email!,
                FullName = user.FullName,
                Phone = user.Phone,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                Roles = roles.ToList(),
                VehicleCount = user.Vehicles?.Count ?? 0,
                AddressCount = user.Addresses?.Count ?? 0,
                BookingCount = user.Bookings?.Count ?? 0,
                WalletBalance = user.Wallet?.Balance
            };
            
            _logger.LogInfo($"GetUserByIdAsync: Successfully retrieved user ID={id}, Email='{user.Email}', Roles={string.Join(",", roles)}");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"GetUserByIdAsync: Database error while retrieving user with ID={id}", ex);
            throw;
        }
    }

    /// <summary>
    /// Creates a new user with password and role
    /// </summary>
    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request)
    {
        try
        {
            _logger.LogInfo($"CreateUserAsync: Starting user creation - Email='{request.Email}', FullName='{request.FullName}', Role='{request.Role ?? "User"}'");
            
            // Create the user entity
            var user = new User
            {
                UserName = request.Email,
                Email = request.Email,
                FullName = request.FullName,
                Phone = request.Phone,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // Create user with password using UserManager
            var createResult = await _userManager.CreateAsync(user, request.Password);
            
            if (!createResult.Succeeded)
            {
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                _logger.LogError($"CreateUserAsync: Failed to create user - {errors}");
                throw new InvalidOperationException($"Failed to create user: {errors}");
            }

            _logger.LogInfo($"CreateUserAsync: User created with ID={user.Id}");

            // Assign role (default to "User" if not specified)
            var role = string.IsNullOrWhiteSpace(request.Role) ? "User" : request.Role;
            var roleResult = await _userManager.AddToRoleAsync(user, role);
            
            if (!roleResult.Succeeded)
            {
                _logger.LogWarning($"CreateUserAsync: Failed to assign role '{role}' to user {user.Id}");
            }
            else
            {
                _logger.LogInfo($"CreateUserAsync: Assigned role '{role}' to user {user.Id}");
            }

            // Return response
            var roles = await _userManager.GetRolesAsync(user);
            var response = new UserResponse
            {
                Id = user.Id,
                Email = user.Email!,
                FullName = user.FullName,
                Phone = user.Phone,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                Roles = roles.ToList(),
                VehicleCount = 0,
                AddressCount = 0,
                BookingCount = 0,
                WalletBalance = null
            };
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"CreateUserAsync: Unexpected error while creating user Email='{request.Email}'", ex);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing user
    /// </summary>
    public async Task<UserResponse?> UpdateUserAsync(Guid id, UpdateUserRequest request)
    {
        try
        {
            _logger.LogInfo($"UpdateUserAsync: Starting update for user ID={id}");
            
            var user = await _userManager.FindByIdAsync(id.ToString());
            
            if (user is null)
            {
                _logger.LogWarning($"UpdateUserAsync: Cannot update - User with ID={id} not found");
                return null;
            }

            // Update email if provided
            if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != user.Email)
            {
                var emailResult = await _userManager.SetEmailAsync(user, request.Email);
                if (!emailResult.Succeeded)
                {
                    var errors = string.Join(", ", emailResult.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Failed to update email: {errors}");
                }
                user.UserName = request.Email; // Keep username in sync with email
                _logger.LogInfo($"UpdateUserAsync: Updated email for user {id}");
            }

            // Update other fields if provided
            if (!string.IsNullOrWhiteSpace(request.FullName))
            {
                user.FullName = request.FullName;
            }

            if (request.Phone != null)
            {
                user.Phone = request.Phone;
            }

            if (request.IsActive.HasValue)
            {
                user.IsActive = request.IsActive.Value;
            }

            user.UpdatedAt = DateTime.UtcNow;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to update user: {errors}");
            }

            // Update role if provided
            if (!string.IsNullOrWhiteSpace(request.Role))
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, request.Role);
                _logger.LogInfo($"UpdateUserAsync: Updated role to '{request.Role}' for user {id}");
            }

            _logger.LogInfo($"UpdateUserAsync: User ID={id} updated successfully");

            // Return updated user response
            var roles = await _userManager.GetRolesAsync(user);
            var response = new UserResponse
            {
                Id = user.Id,
                Email = user.Email!,
                FullName = user.FullName,
                Phone = user.Phone,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                Roles = roles.ToList(),
                VehicleCount = user.Vehicles?.Count ?? 0,
                AddressCount = user.Addresses?.Count ?? 0,
                BookingCount = user.Bookings?.Count ?? 0,
                WalletBalance = user.Wallet?.Balance
            };
            
            return response;
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
            
            var user = await _userManager.FindByIdAsync(id.ToString());
            
            if (user is null)
            {
                _logger.LogWarning($"DeleteUserAsync: Cannot delete - User with ID={id} not found");
                return false;
            }

            var userEmail = user.Email;

            _logger.LogInfo($"DeleteUserAsync: Removing user ID={id}, Email='{userEmail}' from database...");
            
            var result = await _userManager.DeleteAsync(user);
            
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogError($"DeleteUserAsync: Failed to delete user - {errors}");
                throw new InvalidOperationException($"Failed to delete user: {errors}");
            }
            
            _logger.LogInfo($"DeleteUserAsync: User ID={id} ('{userEmail}') deleted successfully");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"DeleteUserAsync: Unexpected error while deleting user ID={id}", ex);
            throw;
        }
    }
}
