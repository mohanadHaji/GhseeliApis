using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Validators;
using Microsoft.AspNetCore.Mvc;

namespace GhseeliApis.Controllers;

/// <summary>
/// User management controller
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
    /// Get all users
    /// </summary>
    /// <returns>List of all users</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<User>), StatusCodes.Status200OK)]
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
    /// Get user by ID
    /// </summary>
    /// <param name="id">User ID</param>
    /// <returns>User details</returns>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserById(int id)
    {
        _logger.LogInfo($"GET /api/users/{id} - Request received to retrieve user");
        
        // Validate input using IdValidator
        var idValidation = IdValidator.ValidateId(id);
        if (!idValidation.IsValid)
        {
            _logger.LogWarning($"GET /api/users/{id} - Validation failed: {string.Join(", ", idValidation.Errors)}");
            return BadRequest(new
            {
                Message = "Validation failed",
                Errors = idValidation.Errors
            });
        }

        try
        {
            var user = await _userHandler.GetUserByIdAsync(id);
            
            if (user is null)
            {
                _logger.LogWarning($"GET /api/users/{id} - User not found, returning 404 Not Found");
                return NotFound();
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
    /// Create a new user
    /// </summary>
    /// <param name="user">User details</param>
    /// <returns>Created user</returns>
    [HttpPost]
    [ProducesResponseType(typeof(User), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateUser([FromBody] User user)
    {
        _logger.LogInfo($"POST /api/users - Request received to create user: Name='{user?.Name}', Email='{user?.Email}'");
        
        if (user == null)
        {
            _logger.LogWarning("POST /api/users - Request body is null or invalid");
            return BadRequest(new { Message = "User data is required" });
        }

        // Validate the user model
        var validationResult = user.Validate();
        if (!validationResult.IsValid)
        {
            _logger.LogWarning($"POST /api/users - Model validation failed: {string.Join(", ", validationResult.Errors)}");
            return BadRequest(new
            {
                Message = "Validation failed",
                Errors = validationResult.Errors
            });
        }

        try
        {
            var createdUser = await _userHandler.CreateUserAsync(user);
            
            _logger.LogInfo($"POST /api/users - User created successfully with ID={createdUser.Id}, returning 201 Created");
            return CreatedAtAction(nameof(GetUserById), new { id = createdUser.Id }, createdUser);
        }
        catch (Exception ex)
        {
            _logger.LogError($"POST /api/users - Failed to create user: Name='{user.Name}', Email='{user.Email}'", ex);
            return StatusCode(500, new { Message = "An error occurred while creating the user" });
        }
    }

    /// <summary>
    /// Update an existing user
    /// </summary>
    /// <param name="id">User ID</param>
    /// <param name="updatedUser">Updated user details</param>
    /// <returns>Updated user</returns>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] User updatedUser)
    {
        _logger.LogInfo($"PUT /api/users/{id} - Request received to update user with new data: Name='{updatedUser?.Name}', Email='{updatedUser?.Email}'");
        
        if (updatedUser == null)
        {
            _logger.LogWarning($"PUT /api/users/{id} - Request body is null or invalid");
            return BadRequest(new { Message = "User data is required" });
        }

        // Validate input ID using IdValidator
        var idValidation = IdValidator.ValidateId(id);
        if (!idValidation.IsValid)
        {
            _logger.LogWarning($"PUT /api/users/{id} - ID validation failed: {string.Join(", ", idValidation.Errors)}");
            return BadRequest(new
            {
                Message = "Validation failed",
                Errors = idValidation.Errors
            });
        }

        // Validate the updated user model
        var validationResult = updatedUser.Validate();
        if (!validationResult.IsValid)
        {
            _logger.LogWarning($"PUT /api/users/{id} - Model validation failed: {string.Join(", ", validationResult.Errors)}");
            return BadRequest(new
            {
                Message = "Validation failed",
                Errors = validationResult.Errors
            });
        }

        try
        {
            var user = await _userHandler.UpdateUserAsync(id, updatedUser);
            
            if (user is null)
            {
                _logger.LogWarning($"PUT /api/users/{id} - User not found, returning 404 Not Found");
                return NotFound();
            }

            _logger.LogInfo($"PUT /api/users/{id} - User updated successfully, returning 200 OK");
            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError($"PUT /api/users/{id} - Failed to update user", ex);
            return StatusCode(500, new { Message = "An error occurred while updating the user" });
        }
    }

    /// <summary>
    /// Delete a user
    /// </summary>
    /// <param name="id">User ID</param>
    /// <returns>No content</returns>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(int id)
    {
        _logger.LogInfo($"DELETE /api/users/{id} - Request received to delete user");
        
        // Validate input using IdValidator
        var idValidation = IdValidator.ValidateId(id);
        if (!idValidation.IsValid)
        {
            _logger.LogWarning($"DELETE /api/users/{id} - Validation failed: {string.Join(", ", idValidation.Errors)}");
            return BadRequest(new
            {
                Message = "Validation failed",
                idValidation.Errors
            });
        }

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
}
