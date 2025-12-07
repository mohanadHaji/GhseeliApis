using GhseeliApis.DTOs.Auth;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;
    private readonly IAppLogger _logger;

    public AuthController(
        IAuthService authService, 
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        IAppLogger logger)
    {
        _authService = authService;
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            _logger.LogInfo($"POST /api/auth/register - Registering user {request.Email}");

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _authService.RegisterAsync(request);
            if (result == null)
            {
                return BadRequest(new { Message = "Registration failed. Email may already be in use or password doesn't meet requirements." });
            }

            _logger.LogInfo($"User registered successfully: {request.Email}");
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error during user registration", ex);
            return StatusCode(500, new { Message = "An error occurred during registration" });
        }
    }

    /// <summary>
    /// Login with email and password
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            _logger.LogInfo($"POST /api/auth/login - Login attempt for {request.Email}");

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _authService.LoginAsync(request);
            if (result == null)
            {
                return Unauthorized(new { Message = "Invalid email or password" });
            }

            _logger.LogInfo($"User logged in successfully: {request.Email}");
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error during user login", ex);
            return StatusCode(500, new { Message = "An error occurred during login" });
        }
    }

    /// <summary>
    /// Validate JWT token
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateToken([FromBody] string token)
    {
        try
        {
            _logger.LogInfo("POST /api/auth/validate - Validating token");

            if (string.IsNullOrEmpty(token))
            {
                return BadRequest(new { Message = "Token is required" });
            }

            var isValid = await _authService.ValidateTokenAsync(token);
            if (!isValid)
            {
                return Unauthorized(new { Message = "Invalid or expired token" });
            }

            return Ok(new { IsValid = true, Message = "Token is valid" });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error during token validation", ex);
            return StatusCode(500, new { Message = "An error occurred during token validation" });
        }
    }

    /// <summary>
    /// Get current authenticated user information
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult GetCurrentUser()
    {
        try
        {
            _logger.LogInfo("GET /api/auth/me - Getting current user");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = User.FindFirstValue(ClaimTypes.Email);
            var fullName = User.FindFirstValue(ClaimTypes.Name);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { Message = "User not authenticated" });
            }

            return Ok(new
            {
                UserId = Guid.Parse(userId),
                Email = email,
                FullName = fullName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting current user", ex);
            return StatusCode(500, new { Message = "An error occurred" });
        }
    }

    #region OAuth 2.0 External Login Endpoints

    /// <summary>
    /// Initiate external login (Google/Facebook)
    /// </summary>
    [HttpGet("external-login")]
    public IActionResult ExternalLogin([FromQuery] string provider, [FromQuery] string? returnUrl = null)
    {
        try
        {
            _logger.LogInfo($"GET /api/auth/external-login - Initiating {provider} login");

            if (string.IsNullOrEmpty(provider))
            {
                return BadRequest(new { Message = "Provider is required" });
            }

            // Configure redirect URL for OAuth callback
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Auth", new { returnUrl }, Request.Scheme);
            
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            
            _logger.LogInfo($"Redirecting to {provider} for authentication");
            return Challenge(properties, provider);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error initiating external login with {provider}", ex);
            return StatusCode(500, new { Message = "An error occurred during external login" });
        }
    }

    /// <summary>
    /// Handle OAuth callback from external provider
    /// </summary>
    [HttpGet("external-login-callback")]
    public async Task<IActionResult> ExternalLoginCallback([FromQuery] string? returnUrl = null)
    {
        try
        {
            _logger.LogInfo("GET /api/auth/external-login-callback - Processing OAuth callback");

            // Get external login info
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                _logger.LogWarning("External login info not found");
                return BadRequest(new { Message = "External login information not found" });
            }

            _logger.LogInfo($"Processing external login callback from {info.LoginProvider}");

            // Process the external login
            var result = await _authService.ExternalLoginCallbackAsync(info);
            if (result == null)
            {
                return BadRequest(new { Message = "External login failed. Please ensure your account has an email address." });
            }

            _logger.LogInfo($"External login successful for {result.Email} via {info.LoginProvider}");

            // If returnUrl is provided, redirect to it (for frontend integration)
            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect($"{returnUrl}?token={result.Token}");
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error during external login callback", ex);
            return StatusCode(500, new { Message = "An error occurred during external login" });
        }
    }

    /// <summary>
    /// Link external login provider to current authenticated user
    /// </summary>
    [HttpPost("link-external-login")]
    [Authorize]
    public IActionResult LinkExternalLogin([FromBody] ExternalLoginRequest request)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInfo($"POST /api/auth/link-external-login - User {userId} linking {request.Provider}");

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { Message = "User not authenticated" });
            }

            // Configure redirect URL for OAuth callback
            var redirectUrl = Url.Action(nameof(LinkExternalLoginCallback), "Auth", new { request.ReturnUrl }, Request.Scheme);
            
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(request.Provider, redirectUrl);
            properties.Items["UserId"] = userId; // Store userId for callback
            
            _logger.LogInfo($"Redirecting user {userId} to {request.Provider} for linking");
            return Challenge(properties, request.Provider);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error initiating link external login", ex);
            return StatusCode(500, new { Message = "An error occurred" });
        }
    }

    /// <summary>
    /// Handle OAuth callback for linking external provider
    /// </summary>
    [HttpGet("link-external-login-callback")]
    [Authorize]
    public async Task<IActionResult> LinkExternalLoginCallback([FromQuery] string? returnUrl = null)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInfo($"GET /api/auth/link-external-login-callback - Processing link callback for user {userId}");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { Message = "User not authenticated" });
            }

            // Get external login info
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                _logger.LogWarning("External login info not found for linking");
                return BadRequest(new { Message = "External login information not found" });
            }

            // Link the external login
            var success = await _authService.LinkExternalLoginAsync(Guid.Parse(userId), info);
            if (!success)
            {
                return BadRequest(new { Message = "Failed to link external login. It may already be linked to another account." });
            }

            _logger.LogInfo($"Successfully linked {info.LoginProvider} to user {userId}");

            // If returnUrl is provided, redirect to it
            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect($"{returnUrl}?linked=true");
            }

            return Ok(new { Message = $"{info.LoginProvider} linked successfully", Provider = info.LoginProvider });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error during link external login callback", ex);
            return StatusCode(500, new { Message = "An error occurred" });
        }
    }

    /// <summary>
    /// Remove external login provider from current user
    /// </summary>
    [HttpDelete("external-login/{provider}")]
    [Authorize]
    public async Task<IActionResult> RemoveExternalLogin(string provider)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInfo($"DELETE /api/auth/external-login/{provider} - User {userId} removing {provider}");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { Message = "User not authenticated" });
            }

            if (string.IsNullOrEmpty(provider))
            {
                return BadRequest(new { Message = "Provider is required" });
            }

            var success = await _authService.RemoveExternalLoginAsync(Guid.Parse(userId), provider);
            if (!success)
            {
                return BadRequest(new { Message = $"Failed to remove {provider}. It may not be linked to your account." });
            }

            _logger.LogInfo($"Successfully removed {provider} from user {userId}");
            return Ok(new { Message = $"{provider} removed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error removing external login {provider}", ex);
            return StatusCode(500, new { Message = "An error occurred" });
        }
    }

    /// <summary>
    /// Get all external logins linked to current user
    /// </summary>
    [HttpGet("external-logins")]
    [Authorize]
    public async Task<IActionResult> GetExternalLogins()
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInfo($"GET /api/auth/external-logins - Getting external logins for user {userId}");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { Message = "User not authenticated" });
            }

            var logins = await _authService.GetExternalLoginsAsync(Guid.Parse(userId));
            
            _logger.LogInfo($"Retrieved {logins.Count} external logins for user {userId}");
            return Ok(logins);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting external logins", ex);
            return StatusCode(500, new { Message = "An error occurred" });
        }
    }

    #endregion
}
