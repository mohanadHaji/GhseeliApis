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
    /// <returns>List of all users</returns>
    Task<List<User>> GetAllUsersAsync();

    /// <summary>
    /// Gets a user by ID
    /// </summary>
    /// <param name="id">User ID</param>
    /// <returns>User if found, null otherwise</returns>
    Task<User?> GetUserByIdAsync(Guid id);

    /// <summary>
    /// Creates a new user
    /// </summary>
    /// <param name="user">User to create</param>
    /// <returns>Created user with generated ID</returns>
    Task<User> CreateUserAsync(User user);

    /// <summary>
    /// Updates an existing user
    /// </summary>
    /// <param name="id">User ID to update</param>
    /// <param name="updatedUser">Updated user data</param>
    /// <returns>Updated user if found, null otherwise</returns>
    Task<User?> UpdateUserAsync(Guid id, User updatedUser);

    /// <summary>
    /// Deletes a user by ID
    /// </summary>
    /// <param name="id">User ID to delete</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteUserAsync(Guid id);
}
