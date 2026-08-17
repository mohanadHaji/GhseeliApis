using GhseeliApis.DTOs.User;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GhseeliApis.Controllers;

/// <summary>
/// User management controller
/// Note: User creation (POST) is public for self-registration
/// All other endpoints require Admin role
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Tags("Users")]
public class UsersController : ControllerBase
{
    private readonly IUserHandler _userHandler;
    private readonly IAppLogger _logger;

    public UsersController(IUserHandler userHandler, IAppLogger logger)
    {
        _userHandler = userHandler;
        _logger = logger;
    }

    /// <summary>
    /// Get all users with their roles (Admin only)
    /// </summary>
    /// <returns>List of all users</returns>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(List<UserListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllUsers()
    {
        _logger.LogInfo("GET /api/users - Request received to retrieve all users");
        
        try
        {
            var users = await _userHandler.GetAllUsersAsync();
            
            _logger.LogInfo($"GET /api/users - Returning {users.Count} user(s) with status 200 OK");
            return Ok(users);
        }
        catch (Exception ex)
        {
            _logger.LogError("GET /api/users - Internal server error occurred", ex);
            return StatusCode(500, new { Message = "An error occurred while retrieving users" });
        }
    }

    /// <summary>
    /// Get user by ID with detailed information (Admin only, or own profile)
    /// </summary>
    /// <param name="id">User ID</param>
    /// <returns>User details including counts and wallet balance</returns>
    /// <remarks>
    /// Users can view their own profile. Admins can view any user profile.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [Authorize] // Any authenticated user
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUserById(Guid id)
    {
        _logger.LogInfo($"GET /api/users/{id} - Request received to retrieve user");

        try
        {
            var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isAdmin = User.IsInRole("Admin");

            // Check if user is accessing their own profile or is admin
            if (id != currentUserId && !isAdmin)
            {
                _logger.LogWarning($"GET /api/users/{id} - User {currentUserId} attempted to access another user's profile without admin rights");
                return StatusCode(403, new { Message = "You can only view your own profile unless you are an admin" });
            }

            var user = await _userHandler.GetUserByIdAsync(id);
            
            if (user is null)
            {
                _logger.LogWarning($"GET /api/users/{id} - User not found, returning 404 Not Found");
                return NotFound(new { Message = "User not found" });
            }

            _logger.LogInfo($"GET /api/users/{id} - User found, returning 200 OK");
            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError($"GET /api/users/{id} - Internal server error occurred", ex);
            return StatusCode(500, new { Message = "An error occurred while retrieving the user" });
        }
    }

    /// <summary>
    /// Create a new user (Public endpoint for self-registration)
    /// </summary>
    /// <param name="request">User creation request with email, password, name, and optional role</param>
    /// <returns>Created user details</returns>
    /// <remarks>
    /// This endpoint is public to allow self-registration.
    /// If no role is specified, user will be assigned the "User" role by default.
    /// To create users with elevated roles (Company, Admin), admin privileges are required via AuthController.
    /// </remarks>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest? request)
    {
        if (request == null)
        {
            _logger.LogWarning("POST /api/users - Request body is null");
            return BadRequest(new { Message = "Request body is required" });
        }

        _logger.LogInfo($"POST /api/users - Request received to create user: Email='{request.Email}', FullName='{request.FullName}', Role='{request.Role ?? "User"}'");
        
        if (!ModelState.IsValid)
        {
            _logger.LogWarning($"POST /api/users - Model validation failed");
            return BadRequest(ModelState);
        }

        // SECURITY: Prevent role escalation during self-registration
        // Only allow "User" role for public registration
        if (!string.IsNullOrEmpty(request.Role) && !request.Role.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning($"POST /api/users - Attempt to self-register with elevated role '{request.Role}' by email '{request.Email}'");
            return BadRequest(new { Message = "Cannot self-register with elevated roles. Contact an administrator to request elevated privileges." });
        }

        // Force "User" role for all public registrations
        request.Role = "User";

        try
        {
            var createdUser = await _userHandler.CreateUserAsync(request);
            
            _logger.LogInfo($"POST /api/users - User created successfully with ID={createdUser.Id}, returning 201 Created");
            return CreatedAtAction(nameof(GetUserById), new { id = createdUser.Id }, createdUser);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"POST /api/users - Failed to create user: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"POST /api/users - Failed to create user: Email='{request.Email}'", ex);
            return StatusCode(500, new { Message = "An error occurred while creating the user" });
        }
    }

    /// <summary>
    /// Update an existing user (Admin only)
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="request">Updated user details (only provided fields will be updated)</param>
    /// <returns>Updated user</returns>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
    {
        _logger.LogInfo($"PUT /api/users/{id} - Request received to update user");
        
        if (!ModelState.IsValid)
        {
            _logger.LogWarning($"PUT /api/users/{id} - Model validation failed");
            return BadRequest(ModelState);
        }

        try
        {
            var user = await _userHandler.UpdateUserAsync(id, request);
            
            if (user is null)
            {
                _logger.LogWarning($"PUT /api/users/{id} - User not found, returning 404 Not Found");
                return NotFound(new { Message = "User not found" });
            }

            _logger.LogInfo($"PUT /api/users/{id} - User updated successfully, returning 200 OK");
            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"PUT /api/users/{id} - Failed to update user: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"PUT /api/users/{id} - Failed to update user", ex);
            return StatusCode(500, new { Message = "An error occurred while updating the user" });
        }
    }

    /// <summary>
    /// Delete a user (Admin only)
    /// </summary>
    /// <param name="id">User ID</param>
    /// <returns>No content</returns>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        _logger.LogInfo($"DELETE /api/users/{id} - Request received to delete user");

        try
        {
            var deleted = await _userHandler.DeleteUserAsync(id);
            
            if (!deleted)
            {
                _logger.LogWarning($"DELETE /api/users/{id} - User not found, returning 404 Not Found");
                return NotFound();
            }

            _logger.LogInfo($"DELETE /api/users/{id} - User deleted successfully, returning 204 No Content");
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError($"DELETE /api/users/{id} - Failed to delete user", ex);
            return StatusCode(500, new { Message = "An error occurred while deleting the user" });
        }
    }

    // ========================================
    // SELF-SERVICE ENDPOINTS (Authenticated Users)
    // ========================================

    /// <summary>
    /// Get current authenticated user's profile
    /// </summary>
    /// <returns>Current user's profile details</returns>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyProfile()
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            _logger.LogInfo($"GET /api/users/me - User {userId} requesting their profile");

            var user = await _userHandler.GetUserByIdAsync(userId);
            
            if (user is null)
            {
                _logger.LogWarning($"GET /api/users/me - User {userId} not found");
                return NotFound(new { Message = "User profile not found" });
            }

            _logger.LogInfo($"GET /api/users/me - Returning profile for user {userId}");
            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError("GET /api/users/me - Internal server error occurred", ex);
            return StatusCode(500, new { Message = "An error occurred while retrieving your profile" });
        }
    }

    /// <summary>
    /// Update current authenticated user's profile
    /// </summary>
    /// <param name="request">Updated profile details</param>
    /// <returns>Updated user profile</returns>
    /// <remarks>
    /// Users can update their own email, full name, and phone number.
    /// Role changes and IsActive status require admin privileges.
    /// </remarks>
    [HttpPut("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateUserRequest request)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            _logger.LogInfo($"PUT /api/users/me - User {userId} updating their profile");
            
            if (!ModelState.IsValid)
            {
                _logger.LogWarning($"PUT /api/users/me - Model validation failed for user {userId}");
                return BadRequest(ModelState);
            }

            // SECURITY: Prevent users from changing their role or active status
            if (request.Role != null || request.IsActive.HasValue)
            {
                _logger.LogWarning($"PUT /api/users/me - User {userId} attempted to change role or active status");
                return BadRequest(new { Message = "Cannot change role or active status. Contact an administrator." });
            }

            var user = await _userHandler.UpdateUserAsync(userId, request);
            
            if (user is null)
            {
                _logger.LogWarning($"PUT /api/users/me - User {userId} not found");
                return NotFound(new { Message = "User profile not found" });
            }

            _logger.LogInfo($"PUT /api/users/me - User {userId} profile updated successfully");
            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"PUT /api/users/me - Failed to update profile: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError("PUT /api/users/me - Failed to update profile", ex);
            return StatusCode(500, new { Message = "An error occurred while updating your profile" });
        }
    }

    /// <summary>
    /// Soft delete current authenticated user's account
    /// </summary>
    /// <returns>No content</returns>
    /// <remarks>
    /// Account will be deactivated immediately and permanently deleted after 30 days.
    /// Use PUT /api/users/me/reactivate to cancel deletion within the grace period.
    /// </remarks>
    [HttpDelete("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteMyAccount()
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            _logger.LogInfo($"DELETE /api/users/me - User {userId} requesting account deletion");

            var deleted = await _userHandler.SoftDeleteUserAsync(userId);
            
            if (!deleted)
            {
                _logger.LogWarning($"DELETE /api/users/me - User {userId} not found");
                return BadRequest(new { Message = "Unable to delete account" });
            }

            _logger.LogInfo($"DELETE /api/users/me - User {userId} account scheduled for deletion");
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError("DELETE /api/users/me - Failed to delete account", ex);
            return StatusCode(500, new { Message = "An error occurred while deleting your account" });
        }
    }

    /// <summary>
    /// Reactivate current authenticated user's account (cancel scheduled deletion)
    /// </summary>
    /// <returns>Success message</returns>
    /// <remarks>
    /// Can only be used within the 30-day grace period after soft deletion.
    /// </remarks>
    [HttpPut("me/reactivate")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReactivateAccount()
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            _logger.LogInfo($"PUT /api/users/me/reactivate - User {userId} requesting account reactivation");

            var reactivated = await _userHandler.ReactivateAccountAsync(userId);

            if (!reactivated)
            {
                _logger.LogWarning($"PUT /api/users/me/reactivate - User {userId} not found");
                return NotFound(new { Message = "User not found" });
            }

            _logger.LogInfo($"PUT /api/users/me/reactivate - User {userId} account reactivated successfully");
            return Ok(new { Message = "Account reactivated successfully. Scheduled deletion has been cancelled." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"PUT /api/users/me/reactivate - Failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError("PUT /api/users/me/reactivate - Failed to reactivate account", ex);
            return StatusCode(500, new { Message = "An error occurred while reactivating your account" });
        }
    }

    /// <summary>
    /// Change current authenticated user's password
    /// </summary>
    /// <param name="request">Current and new password</param>
    /// <returns>Success message</returns>
    [HttpPut("me/password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            _logger.LogInfo($"PUT /api/users/me/password - User {userId} requesting password change");

            if (!ModelState.IsValid)
            {
                _logger.LogWarning($"PUT /api/users/me/password - Model validation failed for user {userId}");
                return BadRequest(ModelState);
            }

            var changed = await _userHandler.ChangePasswordAsync(userId, request);

            if (!changed)
            {
                _logger.LogWarning($"PUT /api/users/me/password - User {userId} not found");
                return NotFound(new { Message = "User not found" });
            }

            _logger.LogInfo($"PUT /api/users/me/password - Password changed for user {userId}");
            return Ok(new { Message = "Password changed successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"PUT /api/users/me/password - Failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError("PUT /api/users/me/password - Failed to change password", ex);
            return StatusCode(500, new { Message = "An error occurred while changing your password" });
        }
    }

    /// <summary>
    /// Request email change verification token
    /// </summary>
    /// <returns>Verification token (in production, this would be emailed)</returns>
    /// <remarks>
    /// First update your profile with the new email via PUT /api/users/me,
    /// then call this endpoint to get a verification token.
    /// </remarks>
    [HttpPost("me/email/request-confirmation")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestEmailConfirmation()
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            _logger.LogInfo($"POST /api/users/me/email/request-confirmation - User {userId} requesting email verification token");

            var token = await _userHandler.GenerateEmailChangeTokenAsync(userId);

            _logger.LogInfo($"POST /api/users/me/email/request-confirmation - Token generated for user {userId}");
            return Ok(new { Token = token, Message = "Verification token generated. In production, this would be sent via email." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"POST /api/users/me/email/request-confirmation - Failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError("POST /api/users/me/email/request-confirmation - Failed", ex);
            return StatusCode(500, new { Message = "An error occurred while generating the verification token" });
        }
    }

    /// <summary>
    /// Confirm email change with verification token
    /// </summary>
    /// <param name="token">The verification token received from request-confirmation</param>
    /// <returns>Success message</returns>
    [HttpPost("me/email/confirm")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmailChange([FromBody] string token)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            _logger.LogInfo($"POST /api/users/me/email/confirm - User {userId} confirming email change");

            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest(new { Message = "Verification token is required" });
            }

            var confirmed = await _userHandler.ConfirmEmailChangeAsync(userId, token);

            if (!confirmed)
            {
                _logger.LogWarning($"POST /api/users/me/email/confirm - User {userId} not found");
                return NotFound(new { Message = "User not found" });
            }

            _logger.LogInfo($"POST /api/users/me/email/confirm - Email changed for user {userId}");
            return Ok(new { Message = "Email changed successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"POST /api/users/me/email/confirm - Failed: {ex.Message}");
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError("POST /api/users/me/email/confirm - Failed to confirm email change", ex);
            return StatusCode(500, new { Message = "An error occurred while confirming your email change" });
        }
    }
}
