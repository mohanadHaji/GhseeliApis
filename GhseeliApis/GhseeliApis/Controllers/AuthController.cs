using GhseeliApis.DTOs.Auth;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IAppLogger _logger;

    public AuthController(IAuthService authService, IAppLogger logger)
    {
        _authService = authService;
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
}
