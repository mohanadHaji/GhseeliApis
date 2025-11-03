using FluentAssertions;
using GhseeliApis.Controllers;
using GhseeliApis.Data;
using GhseeliApis.Handlers;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Unit tests for UsersController
/// </summary>
public class UsersControllerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IAppLogger _logger;
    private readonly IUserHandler _userHandler;
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        // Create an in-memory database for testing
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _logger = new ConsoleLogger();
        _userHandler = new UserHandler(_context, _logger);
        _controller = new UsersController(_userHandler, _logger);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    #region GetAllUsers Tests

    [Fact]
    public async Task GetAllUsers_ReturnsEmptyList_WhenNoUsersExist()
    {
        // Act
        var result = await _controller.GetAllUsers();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var users = okResult.Value.Should().BeAssignableTo<List<User>>().Subject;
        users.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllUsers_ReturnsAllUsers_WhenUsersExist()
    {
        // Arrange
        var testUsers = new List<User>
        {
            new User { Name = "User 1", Email = "user1@test.com", IsActive = true },
            new User { Name = "User 2", Email = "user2@test.com", IsActive = false },
            new User { Name = "User 3", Email = "user3@test.com", IsActive = true }
        };
        _context.Users.AddRange(testUsers);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetAllUsers();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var users = okResult.Value.Should().BeAssignableTo<List<User>>().Subject;
        users.Should().HaveCount(3);
        users.Should().Contain(u => u.Name == "User 1");
        users.Should().Contain(u => u.Name == "User 2");
        users.Should().Contain(u => u.Name == "User 3");
    }

    #endregion

    #region GetUserById Tests

    [Fact]
    public async Task GetUserById_ReturnsBadRequest_WhenIdIsZero()
    {
        // Act
        var result = await _controller.GetUserById(0);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetUserById_ReturnsBadRequest_WhenIdIsNegative()
    {
        // Act
        var result = await _controller.GetUserById(-1);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
        
        var response = badRequestResult.Value;
        var errorsProperty = response?.GetType().GetProperty("Errors");
        errorsProperty.Should().NotBeNull();
        var errors = errorsProperty!.GetValue(response) as List<string>;
        errors.Should().Contain("User ID must be greater than zero.");
    }

    [Fact]
    public async Task GetUserById_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Act
        var result = await _controller.GetUserById(999);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetUserById_ReturnsUser_WhenUserExists()
    {
        // Arrange
        var testUser = new User
        {
            Name = "Test User",
            Email = "test@example.com",
            IsActive = true
        };
        _context.Users.Add(testUser);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetUserById(testUser.Id);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var user = okResult.Value.Should().BeOfType<User>().Subject;
        user.Id.Should().Be(testUser.Id);
        user.Name.Should().Be("Test User");
        user.Email.Should().Be("test@example.com");
        user.IsActive.Should().BeTrue();
    }

    #endregion

    #region CreateUser Tests

    [Fact]
    public async Task CreateUser_CreatesNewUser_AndReturnsCreatedResult()
    {
        // Arrange
        var newUser = new User
        {
            Name = "New User",
            Email = "newuser@example.com",
            IsActive = true
        };

        // Act
        var result = await _controller.CreateUser(newUser);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(UsersController.GetUserById));
        
        var returnedUser = createdResult.Value.Should().BeOfType<User>().Subject;
        returnedUser.Name.Should().Be("New User");
        returnedUser.Email.Should().Be("newuser@example.com");
        returnedUser.IsActive.Should().BeTrue();
        returnedUser.Id.Should().BeGreaterThan(0);

        // Verify user was actually saved to database
        var savedUser = await _context.Users.FindAsync(returnedUser.Id);
        savedUser.Should().NotBeNull();
        savedUser!.Name.Should().Be("New User");
    }

    [Fact]
    public async Task CreateUser_SetsCreatedAtTimestamp()
    {
        // Arrange
        var newUser = new User
        {
            Name = "New User",
            Email = "newuser@example.com",
            IsActive = true
        };

        // Act
        var result = await _controller.CreateUser(newUser);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var returnedUser = createdResult.Value.Should().BeOfType<User>().Subject;
        returnedUser.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateUser_ReturnsBadRequest_WhenNameIsEmpty()
    {
        // Arrange
        var newUser = new User
        {
            Name = "",
            Email = "test@example.com"
        };

        // Act
        var result = await _controller.CreateUser(newUser);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateUser_ReturnsBadRequest_WhenEmailIsInvalid()
    {
        // Arrange
        var newUser = new User
        {
            Name = "Test User",
            Email = "notanemail"
        };

        // Act
        var result = await _controller.CreateUser(newUser);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateUser_ReturnsValidationErrors_WhenModelIsInvalid()
    {
        // Arrange
        var newUser = new User
        {
            Name = "", // Invalid
            Email = "invalid" // Invalid
        };

        // Act
        var result = await _controller.CreateUser(newUser);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = badRequestResult.Value;
        
        var messageProperty = response?.GetType().GetProperty("Message");
        messageProperty.Should().NotBeNull();
        messageProperty!.GetValue(response).Should().Be("Validation failed");
        
        var errorsProperty = response?.GetType().GetProperty("Errors");
        errorsProperty.Should().NotBeNull();
        var errors = errorsProperty!.GetValue(response) as List<string>;
        errors.Should().NotBeNull();
        errors.Should().HaveCountGreaterThan(0);
    }

    #endregion

    #region UpdateUser Tests

    [Fact]
    public async Task UpdateUser_ReturnsBadRequest_WhenIdIsZero()
    {
        // Arrange
        var updatedUser = new User
        {
            Name = "Updated Name",
            Email = "updated@example.com",
            IsActive = false
        };

        // Act
        var result = await _controller.UpdateUser(0, updatedUser);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UpdateUser_ReturnsBadRequest_WhenIdIsNegative()
    {
        // Arrange
        var updatedUser = new User
        {
            Name = "Updated Name",
            Email = "updated@example.com",
            IsActive = false
        };

        // Act
        var result = await _controller.UpdateUser(-1, updatedUser);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
        
        var response = badRequestResult.Value;
        var errorsProperty = response?.GetType().GetProperty("Errors");
        errorsProperty.Should().NotBeNull();
        var errors = errorsProperty!.GetValue(response) as List<string>;
        errors.Should().Contain("User ID must be greater than zero.");
    }

    [Fact]
    public async Task UpdateUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var updatedUser = new User
        {
            Name = "Updated Name",
            Email = "updated@example.com",
            IsActive = false
        };
        _context.Users.Add(updatedUser);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.UpdateUser(999, updatedUser);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateUser_UpdatesUser_WhenUserExists()
    {
        // Arrange
        var existingUser = new User
        {
            Name = "Original Name",
            Email = "original@example.com",
            IsActive = true
        };
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var updatedData = new User
        {
            Name = "Updated Name",
            Email = "updated@example.com",
            IsActive = false
        };

        // Act
        var result = await _controller.UpdateUser(existingUser.Id, updatedData);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedUser = okResult.Value.Should().BeOfType<User>().Subject;
        
        returnedUser.Id.Should().Be(existingUser.Id);
        returnedUser.Name.Should().Be("Updated Name");
        returnedUser.Email.Should().Be("updated@example.com");
        returnedUser.IsActive.Should().BeFalse();
        returnedUser.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UpdateUser_PersistsChangesToDatabase()
    {
        // Arrange
        var existingUser = new User
        {
            Name = "Original Name",
            Email = "original@example.com",
            IsActive = true
        };
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var updatedData = new User
        {
            Name = "Updated Name",
            Email = "updated@example.com",
            IsActive = false
        };

        // Act
        await _controller.UpdateUser(existingUser.Id, updatedData);

        // Assert
        var savedUser = await _context.Users.FindAsync(existingUser.Id);
        savedUser.Should().NotBeNull();
        savedUser!.Name.Should().Be("Updated Name");
        savedUser.Email.Should().Be("updated@example.com");
        savedUser.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateUser_ReturnsBadRequest_WhenValidationFails()
    {
        // Arrange
        var existingUser = new User
        {
            Name = "Original Name",
            Email = "original@example.com",
            IsActive = true
        };
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var invalidUpdate = new User
        {
            Name = "", // Invalid
            Email = "notanemail", // Invalid
            IsActive = true
        };

        // Act
        var result = await _controller.UpdateUser(existingUser.Id, invalidUpdate);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    #endregion

    #region DeleteUser Tests

    [Fact]
    public async Task DeleteUser_ReturnsBadRequest_WhenIdIsZero()
    {
        // Act
        var result = await _controller.DeleteUser(0);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task DeleteUser_ReturnsBadRequest_WhenIdIsNegative()
    {
        // Act
        var result = await _controller.DeleteUser(-1);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
        
        var response = badRequestResult.Value;
        var errorsProperty = response?.GetType().GetProperty("Errors");
        errorsProperty.Should().NotBeNull();
        var errors = errorsProperty!.GetValue(response) as List<string>;
        errors.Should().Contain("User ID must be greater than zero.");
    }

    [Fact]
    public async Task DeleteUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Act
        var result = await _controller.DeleteUser(999);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteUser_ReturnsNoContent_WhenUserExists()
    {
        // Arrange
        var testUser = new User
        {
            Name = "Test User",
            Email = "test@example.com",
            IsActive = true
        };
        _context.Users.Add(testUser);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteUser(testUser.Id);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteUser_RemovesUserFromDatabase()
    {
        // Arrange
        var testUser = new User
        {
            Name = "Test User",
            Email = "test@example.com",
            IsActive = true
        };
        _context.Users.Add(testUser);
        await _context.SaveChangesAsync();
        var userId = testUser.Id;

        // Act
        await _controller.DeleteUser(userId);

        // Assert
        var deletedUser = await _context.Users.FindAsync(userId);
        deletedUser.Should().BeNull();
    }

    [Fact]
    public async Task DeleteUser_OnlyDeletesSpecificUser()
    {
        // Arrange
        var user1 = new User { Name = "User 1", Email = "user1@test.com", IsActive = true };
        var user2 = new User { Name = "User 2", Email = "user2@test.com", IsActive = true };
        _context.Users.AddRange(user1, user2);
        await _context.SaveChangesAsync();

        // Act
        await _controller.DeleteUser(user1.Id);

        // Assert
        var deletedUser = await _context.Users.FindAsync(user1.Id);
        var remainingUser = await _context.Users.FindAsync(user2.Id);
        
        deletedUser.Should().BeNull();
        remainingUser.Should().NotBeNull();
        remainingUser!.Name.Should().Be("User 2");
    }

    #endregion
}
