using GhseeliApis.DTOs.Auth;

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
}
