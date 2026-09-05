using GhseeliApis.Constants;
using GhseeliApis.DTOs.Auth;
using Ghseeli.Common.Logging;
using GhseeliApis.Models;
using GhseeliApis.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace GhseeliApis.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly IAppLogger _logger;

    public AuthService(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IConfiguration configuration,
        IAppLogger logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request, string role = "User")
    {
        try
        {
            // Check if user already exists
            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
            {
                _logger.LogWarning("Registration failed: account already exists");
                return null;
            }

            // Validate role
            if (role != AppRoles.User && role != AppRoles.Admin)
            {
                _logger.LogWarning($"Registration failed: Invalid role '{role}'");
                return null;
            }

            // Create new user
            var user = new User
            {
                UserName = request.Email,
                Email = request.Email,
                FullName = request.FullName,
                PhoneNumber = request.PhoneNumber,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                var errorCodes = string.Join(",", result.Errors.Select(e => e.Code));
                _logger.LogWarning($"Registration failed: identityErrors={errorCodes}");
                return null;
            }

            // Assign role to user
            var roleResult = await _userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                var errorCodes = string.Join(",", roleResult.Errors.Select(e => e.Code));
                _logger.LogWarning(
                    $"Failed to assign role '{role}': identityErrors={errorCodes}");
                // Delete user if role assignment fails
                await _userManager.DeleteAsync(user);
                return null;
            }

            _logger.LogInfo(
                $"User registered successfully: userId={user.Id}, role='{role}'");

            // Generate JWT token
            var token = await GenerateJwtTokenAsync(user.Id, user.Email!, user.FullName);
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");

            return new AuthResponse
            {
                UserId = user.Id,
                Email = user.Email!,
                FullName = user.FullName,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                $"Auth registration failed: exceptionType={ex.GetType().Name}, " +
                $"exceptionCode=0x{ex.HResult:X8}");
            throw;
        }
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        try
        {
            // Find user by email
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
            {
                _logger.LogWarning("Login failed: account not found");
                return null;
            }

            // Check if user is active
            if (!user.IsActive)
            {
                _logger.LogWarning($"Login failed: userId={user.Id} is inactive");
                return null;
            }

            // Verify password
            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                if (result.IsLockedOut)
                {
                    _logger.LogWarning($"Login failed: userId={user.Id} is locked out");
                }
                else
                {
                    _logger.LogWarning($"Login failed: invalid password for userId={user.Id}");
                }
                return null;
            }

            _logger.LogInfo($"User logged in successfully: userId={user.Id}");

            // Generate JWT token
            var token = await GenerateJwtTokenAsync(user.Id, user.Email!, user.FullName);
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");

            return new AuthResponse
            {
                UserId = user.Id,
                Email = user.Email!,
                FullName = user.FullName,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                $"Auth login failed: exceptionType={ex.GetType().Name}, " +
                $"exceptionCode=0x{ex.HResult:X8}");
            throw;
        }
    }

    public async Task<string> GenerateJwtTokenAsync(Guid userId, string email, string fullName)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");
        var issuer = jwtSettings["Issuer"];
        var audience = jwtSettings["Audience"];
        var expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"] ?? "60");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        // Get user to retrieve roles
        var user = await _userManager.FindByIdAsync(userId.ToString());
        var roles = user != null ? await _userManager.GetRolesAsync(user) : new List<string>();

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, fullName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        // Add role claims
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(secretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidAudience = jwtSettings["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero
            };

            tokenHandler.ValidateToken(token, validationParameters, out _);
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                $"Token validation failed: exceptionType={ex.GetType().Name}, " +
                $"exceptionCode=0x{ex.HResult:X8}");
            return false;
        }
    }

    #region OAuth 2.0 External Login Methods

    public async Task<ExternalLoginCallbackResponse?> ExternalLoginCallbackAsync(ExternalLoginInfo info)
    {
        try
        {
            // Extract email from claims
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("External login failed: email claim is missing");
                return null;
            }

            // Extract name from claims
            var name = info.Principal.FindFirstValue(ClaimTypes.Name) 
                       ?? info.Principal.FindFirstValue("name")
                       ?? email.Split('@')[0];

            // Check if user exists
            var user = await _userManager.FindByEmailAsync(email);
            bool isNewUser = false;

            if (user == null)
            {
                // Create new user
                user = new User
                {
                    UserName = email,
                    Email = email,
                    FullName = name,
                    EmailConfirmed = true, // Email is confirmed by OAuth provider
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    LogIdentityWarning(
                        "External login user creation failed",
                        createResult.Errors);
                    return null;
                }

                // Assign default "User" role
                var roleResult = await _userManager.AddToRoleAsync(user, AppRoles.User);
                if (!roleResult.Succeeded)
                {
                    LogIdentityWarning(
                        $"External login role assignment failed: userId={user.Id}",
                        roleResult.Errors);
                }

                isNewUser = true;
                _logger.LogInfo(
                    $"New user created from external login: userId={user.Id}");
            }

            // Check if external login is already linked
            var existingLogin = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (existingLogin == null)
            {
                // Link external login to user
                var addLoginResult = await _userManager.AddLoginAsync(user, info);
                if (!addLoginResult.Succeeded)
                {
                    LogIdentityWarning(
                        $"External login link failed: userId={user.Id}",
                        addLoginResult.Errors);
                    return null;
                }
                _logger.LogInfo(
                    $"External login linked: userId={user.Id}");
            }

            // Generate JWT token
            var token = await GenerateJwtTokenAsync(user.Id, user.Email!, user.FullName);
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");

            return new ExternalLoginCallbackResponse
            {
                IsNewUser = isNewUser,
                UserId = user.Id,
                Email = user.Email!,
                FullName = user.FullName,
                Provider = info.LoginProvider,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes)
            };
        }
        catch (Exception ex)
        {
            LogSanitizedError("external-login callback", ex);
            throw;
        }
    }

    public async Task<bool> LinkExternalLoginAsync(Guid userId, ExternalLoginInfo info)
    {
        try
        {
            // Find user
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                _logger.LogWarning($"Link external login failed: User {userId} not found");
                return false;
            }

            // Check if external login is already linked to another user
            var existingUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (existingUser != null && existingUser.Id != userId)
            {
                _logger.LogWarning(
                    "Link external login failed: provider identity is already linked");
                return false;
            }

            // Check if already linked to this user
            if (existingUser != null && existingUser.Id == userId)
            {
                _logger.LogInfo(
                    $"External login already linked: userId={userId}");
                return true; // Already linked, treat as success
            }

            // Link external login
            var result = await _userManager.AddLoginAsync(user, info);
            if (!result.Succeeded)
            {
                LogIdentityWarning(
                    $"External login link failed: userId={userId}",
                    result.Errors);
                return false;
            }

            _logger.LogInfo($"External login linked: userId={userId}");
            return true;
        }
        catch (Exception ex)
        {
            LogSanitizedError("external-login link", ex, userId);
            throw;
        }
    }

    public async Task<bool> RemoveExternalLoginAsync(Guid userId, string loginProvider)
    {
        try
        {
            // Find user
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                _logger.LogWarning($"Remove external login failed: User {userId} not found");
                return false;
            }

            // Get user's external logins
            var userLogins = await _userManager.GetLoginsAsync(user);
            var loginToRemove = userLogins.FirstOrDefault(l => l.LoginProvider == loginProvider);

            if (loginToRemove == null)
            {
                _logger.LogWarning(
                    $"Remove external login failed: provider identity not found; userId={userId}");
                return false;
            }

            // Remove external login
            var result = await _userManager.RemoveLoginAsync(user, loginProvider, loginToRemove.ProviderKey);
            if (!result.Succeeded)
            {
                LogIdentityWarning(
                    $"External login removal failed: userId={userId}",
                    result.Errors);
                return false;
            }

            _logger.LogInfo($"External login removed: userId={userId}");
            return true;
        }
        catch (Exception ex)
        {
            LogSanitizedError("external-login removal", ex, userId);
            throw;
        }
    }

    public async Task<IList<ExternalLoginInfoDto>> GetExternalLoginsAsync(Guid userId)
    {
        try
        {
            // Find user
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                _logger.LogWarning($"Get external logins failed: User {userId} not found");
                return new List<ExternalLoginInfoDto>();
            }

            // Get user's external logins
            var userLogins = await _userManager.GetLoginsAsync(user);

            // Map to DTOs
            var loginDtos = userLogins.Select(login => new ExternalLoginInfoDto
            {
                LoginProvider = login.LoginProvider,
                ProviderKey = login.ProviderKey,
                ProviderDisplayName = login.ProviderDisplayName
            }).ToList();

            _logger.LogInfo($"Retrieved {loginDtos.Count} external logins for user {userId}");
            return loginDtos;
        }
        catch (Exception ex)
        {
            LogSanitizedError("external-login listing", ex, userId);
            throw;
        }
    }

    #endregion

    private void LogIdentityWarning(
        string operation,
        IEnumerable<IdentityError> errors)
    {
        var errorCodes = string.Join(",", errors.Select(error => error.Code));
        _logger.LogWarning($"{operation}: identityErrors={errorCodes}");
    }

    private void LogSanitizedError(
        string operation,
        Exception exception,
        Guid? userId = null)
    {
        var userContext = userId.HasValue
            ? $", userId={userId.Value}"
            : string.Empty;
        _logger.LogError(
            $"Auth {operation} failed: exceptionType={exception.GetType().Name}, " +
            $"exceptionCode=0x{exception.HResult:X8}{userContext}");
    }
}
