using GhseeliApis.DTOs.User;
using GhseeliApis.Interfaces;
using GhseeliApis.Models;

namespace GhseeliApis.Handlers.Interfaces;

/// <summary>
/// Interface for user-related operations
/// </summary>
public interface IUserHandler
{
    /// <summary>
    /// Gets all users
    /// </summary>
    /// <returns>List of all users with their roles</returns>
    Task<List<UserListResponse>> GetAllUsersAsync();

    /// <summary>
    /// Gets a user by ID with detailed information
    /// </summary>
    /// <param name="id">User ID</param>
    /// <returns>User details if found, null otherwise</returns>
    Task<UserResponse?> GetUserByIdAsync(Guid id);

    /// <summary>
    /// Creates a new user with password and role
    /// </summary>
    /// <param name="request">User creation request</param>
    /// <returns>Created user details</returns>
    Task<UserResponse> CreateUserAsync(CreateUserRequest request);

    /// <summary>
    /// Updates an existing user
    /// </summary>
    /// <param name="id">User ID to update</param>
    /// <param name="request">Updated user data</param>
    /// <returns>Updated user details if found, null otherwise</returns>
    Task<UserResponse?> UpdateUserAsync(Guid id, UpdateUserRequest request);

    /// <summary>
    /// Deletes a user by ID
    /// </summary>
    /// <param name="id">User ID to delete</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteUserAsync(Guid id);
}
