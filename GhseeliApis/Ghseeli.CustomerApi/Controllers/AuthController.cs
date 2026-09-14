using GhseeliApis.DTOs.Auth;
using Ghseeli.Common.Logging;
using GhseeliApis.Models;
using GhseeliApis.Services.Interfaces;
using GhseeliApis.Services.Auth;
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
    private readonly ICustomerOtpAuthenticationService _otpAuthentication;
    private readonly ICustomerRefreshTokenService _refreshTokens;

    public AuthController(
        IAuthService authService, 
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        IAppLogger logger,
        ICustomerOtpAuthenticationService otpAuthentication,
        ICustomerRefreshTokenService refreshTokens)
    {
        _authService = authService;
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
        _otpAuthentication = otpAuthentication;
        _refreshTokens = refreshTokens;
    }

    /// <summary>
    /// Register a new user
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            _logger.LogInfo("POST /api/auth/register - Registration attempt");

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _authService.RegisterAsync(request);
            if (result == null)
            {
                return BadRequest(new { Message = "Registration failed. Email may already be in use or password doesn't meet requirements." });
            }

            Response.Headers.CacheControl = "no-store";
            _logger.LogInfo($"User registered successfully: userId={result.UserId}");
            return Ok(result);
        }
        catch (Exception ex)
        {
            LogSanitizedError("registration request", ex);
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
            _logger.LogInfo("POST /api/auth/login - Login attempt");

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _authService.LoginAsync(request);
            if (result == null)
            {
                return Unauthorized(new { Message = "Invalid email or password" });
            }

            Response.Headers.CacheControl = "no-store";
            _logger.LogInfo($"User logged in successfully: userId={result.UserId}");
            return Ok(result);
        }
        catch (Exception ex)
        {
            LogSanitizedError("login request", ex);
            return StatusCode(500, new { Message = "An error occurred during login" });
        }
    }

    [HttpPost("otp/request")]
    [ProducesResponseType<OtpRequestAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RequestOtp(
        [FromBody] RequestOtpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _otpAuthentication.RequestAsync(request.Email, cancellationToken);
            return Accepted(new OtpRequestAcceptedResponse());
        }
        catch (CustomerOtpException exception)
        {
            return AuthProblem(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("otp/confirm")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConfirmOtp(
        [FromBody] ConfirmOtpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            Response.Headers.CacheControl = "no-store";
            return Ok(await _otpAuthentication.ConfirmAsync(
                request.Email,
                request.Code,
                cancellationToken));
        }
        catch (CustomerOtpException exception)
        {
            return AuthProblem(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var rotated = await _refreshTokens.RotateAsync(
                request.RefreshToken,
                (userId, email, fullName) =>
                    _authService.GenerateJwtTokenAsync(userId, email, fullName),
                cancellationToken);
            var expirationMinutes = int.Parse(
                HttpContext.RequestServices.GetRequiredService<IConfiguration>()
                    ["JwtSettings:ExpirationMinutes"] ?? "60");
            Response.Headers.CacheControl = "no-store";
            return Ok(new AuthResponse
            {
                UserId = rotated.UserId,
                Email = rotated.Email,
                FullName = rotated.FullName,
                Token = rotated.AccessToken,
                ExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes),
                RefreshToken = rotated.Token,
                RefreshTokenExpiresAt = rotated.ExpiresAtUtc,
                IsNewUser = false
            });
        }
        catch (CustomerRefreshTokenException exception)
        {
            return AuthProblem(exception.StatusCode, exception.Code, exception.Message);
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
            LogSanitizedError("token validation", ex);
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
            LogSanitizedError("current-user lookup", ex);
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
            _logger.LogInfo("GET /api/auth/external-login - Initiating external login");

            if (string.IsNullOrEmpty(provider))
            {
                return BadRequest(new { Message = "Provider is required" });
            }

            // Configure redirect URL for OAuth callback
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Auth", new { returnUrl }, Request.Scheme);
            
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            
            _logger.LogInfo("Redirecting to external authentication");
            return Challenge(properties, provider);
        }
        catch (Exception ex)
        {
            LogSanitizedError("external-login initiation", ex);
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

            _logger.LogInfo("Processing external login callback");

            // Process the external login
            var result = await _authService.ExternalLoginCallbackAsync(info);
            if (result == null)
            {
                return BadRequest(new { Message = "External login failed. Please ensure your account has an email address." });
            }

            _logger.LogInfo(
                $"External login successful: userId={result.UserId}");

            // If returnUrl is provided, redirect to it (for frontend integration)
            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect($"{returnUrl}?token={result.Token}");
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            LogSanitizedError("external-login callback", ex);
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
            _logger.LogInfo(
                $"POST /api/auth/link-external-login - User {userId} linking external login");

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
            
            _logger.LogInfo(
                $"Redirecting user {userId} for external login linking");
            return Challenge(properties, request.Provider);
        }
        catch (Exception ex)
        {
            LogSanitizedError("external-login link initiation", ex);
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

            _logger.LogInfo($"Successfully linked external login to user {userId}");

            // If returnUrl is provided, redirect to it
            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect($"{returnUrl}?linked=true");
            }

            return Ok(new { Message = $"{info.LoginProvider} linked successfully", Provider = info.LoginProvider });
        }
        catch (Exception ex)
        {
            LogSanitizedError("external-login link callback", ex);
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
            _logger.LogInfo(
                $"DELETE /api/auth/external-login - User {userId} removing external login");

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

            _logger.LogInfo($"Successfully removed external login from user {userId}");
            return Ok(new { Message = $"{provider} removed successfully" });
        }
        catch (Exception ex)
        {
            LogSanitizedError("external-login removal", ex);
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
            LogSanitizedError("external-login listing", ex);
            return StatusCode(500, new { Message = "An error occurred" });
        }
    }

    #endregion

    private ObjectResult AuthProblem(int statusCode, string code, string detail)
    {
        var hebrew = Request.Headers.AcceptLanguage.ToString()
            .StartsWith("he", StringComparison.OrdinalIgnoreCase);
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = hebrew ? "האימות נכשל." : "فشلت المصادقة.",
            Detail = LocalizedAuthDetail(code, hebrew),
            Type = $"https://api.ghseeli.example/errors/{code}"
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;
        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }

    private static string LocalizedAuthDetail(string code, bool hebrew) =>
        code switch
        {
            CustomerAuthProblemCodes.OtpAttemptLimit => hebrew
                ? "בוצעו יותר מדי ניסיונות אימות שגויים."
                : "تم إجراء عدد كبير جدًا من محاولات التحقق غير الصحيحة.",
            CustomerAuthProblemCodes.OtpDeliveryUnavailable => hebrew
                ? "לא ניתן לשלוח את קוד האימות כעת."
                : "تعذر إرسال رمز التحقق حاليًا.",
            CustomerAuthProblemCodes.RefreshTokenInvalid => hebrew
                ? "אסימון הרענון אינו תקין או שפג תוקפו."
                : "رمز التحديث غير صالح أو منتهي الصلاحية.",
            _ => hebrew
                ? "קוד האימות אינו תקין או שפג תוקפו."
                : "رمز التحقق غير صالح أو منتهي الصلاحية."
        };

    private void LogSanitizedError(string operation, Exception exception)
    {
        _logger.LogError(
            $"Auth {operation} failed: exceptionType={exception.GetType().Name}, " +
            $"exceptionCode=0x{exception.HResult:X8}");
    }
}
