using GhseeliApis.DTOs.Auth;
using Microsoft.AspNetCore.Identity;

namespace GhseeliApis.Services.Interfaces;

public interface IAuthService
{
    /// <summary>
    /// Registers a new user with email/password
    /// </summary>
    Task<AuthResponse?> RegisterAsync(RegisterRequest request, string role = "User");

    /// <summary>
    /// Authenticates user and returns JWT token
    /// </summary>
    Task<AuthResponse?> LoginAsync(LoginRequest request);

    /// <summary>
    /// Generates a JWT token for a user with their roles
    /// </summary>
    Task<string> GenerateJwtTokenAsync(Guid userId, string email, string fullName);

    /// <summary>
    /// Validates if a JWT token is valid and not expired
    /// </summary>
    Task<bool> ValidateTokenAsync(string token);

    // OAuth 2.0 External Login Methods

    /// <summary>
    /// Handles OAuth callback from external provider (Google/Facebook).
    /// Creates new user if doesn't exist, or uses existing user.
    /// Returns JWT token for authenticated user.
    /// </summary>
    /// <param name="info">External login information from the OAuth provider</param>
    /// <returns>Response with JWT token and user details, or null if authentication fails</returns>
    Task<ExternalLoginCallbackResponse?> ExternalLoginCallbackAsync(ExternalLoginInfo info);

    /// <summary>
    /// Links an external login provider to an existing user account
    /// </summary>
    /// <param name="userId">User ID to link the external login to</param>
    /// <param name="info">External login information from the OAuth provider</param>
    /// <returns>True if successfully linked, false otherwise</returns>
    Task<bool> LinkExternalLoginAsync(Guid userId, ExternalLoginInfo info);

    /// <summary>
    /// Removes an external login provider from a user's account
    /// </summary>
    /// <param name="userId">User ID to remove the external login from</param>
    /// <param name="loginProvider">Provider name (e.g., "Google", "Facebook")</param>
    /// <returns>True if successfully removed, false otherwise</returns>
    Task<bool> RemoveExternalLoginAsync(Guid userId, string loginProvider);

    /// <summary>
    /// Gets all external logins linked to a user's account
    /// </summary>
    /// <param name="userId">User ID to get external logins for</param>
    /// <returns>List of external login providers linked to the user</returns>
    Task<IList<ExternalLoginInfoDto>> GetExternalLoginsAsync(Guid userId);
}
