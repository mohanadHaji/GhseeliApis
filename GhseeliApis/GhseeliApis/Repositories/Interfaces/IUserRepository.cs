using GhseeliApis.Models;

namespace GhseeliApis.Repositories.Interfaces;

/// <summary>
/// Repository interface for User entity database operations
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Gets all users from the database
    /// </summary>
    /// <returns>List of all users</returns>
    Task<List<User>> GetAllAsync();

    /// <summary>
    /// Gets a user by ID from the database
    /// </summary>
    /// <param name="id">User ID</param>
    /// <returns>User if found, null otherwise</returns>
    Task<User?> GetByIdAsync(int id);

    /// <summary>
    /// Adds a new user to the database
    /// </summary>
    /// <param name="user">User to add</param>
    /// <returns>The added user with generated ID</returns>
    Task<User> AddAsync(User user);

    /// <summary>
    /// Updates an existing user in the database
    /// </summary>
    /// <param name="user">User to update</param>
    /// <returns>The updated user</returns>
    Task<User> UpdateAsync(User user);

    /// <summary>
    /// Deletes a user from the database
    /// </summary>
    /// <param name="user">User to delete</param>
    /// <returns>Task</returns>
    Task DeleteAsync(User user);

    /// <summary>
    /// Checks if a user exists by ID
    /// </summary>
    /// <param name="id">User ID</param>
    /// <returns>True if exists, false otherwise</returns>
    Task<bool> ExistsAsync(int id);

    /// <summary>
    /// Gets a user by email
    /// </summary>
    /// <param name="email">Email address</param>
    /// <returns>User if found, null otherwise</returns>
    Task<User?> GetByEmailAsync(string email);

    /// <summary>
    /// Gets a user by username
    /// </summary>
    /// <param name="username">Username</param>
    /// <returns>User if found, null otherwise</returns>
    Task<User?> GetByUsernameAsync(string username);
}
