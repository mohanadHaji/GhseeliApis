using FluentAssertions;
using GhseeliApis.Controllers;
using GhseeliApis.Persistence;
using GhseeliApis.Handlers;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
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
    private readonly IUserRepository _repository;
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
        _repository = new UserRepository(_context);
        _userHandler = new UserHandler(_repository, _logger);
        _controller = new UsersController(_userHandler, _logger);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>
    /// Creates a valid test user with all required fields
    /// </summary>
    private static User CreateValidUser(string userName = "testuser", string email = null, string fullName = null)
    {
        email ??= $"{userName}@test.com";
        fullName ??= $"Test {userName}";
        
        return new User
        {
            UserName = userName,
            Email = email,
            FullName = fullName,
            Phone = "1234567890",
            IsActive = true
        };
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
            CreateValidUser("user1", "user1@test.com", "User One"),
            CreateValidUser("user2", "user2@test.com", "User Two"),
            CreateValidUser("user3", "user3@test.com", "User Three")
        };
        _context.Users.AddRange(testUsers);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetAllUsers();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var users = okResult.Value.Should().BeAssignableTo<List<User>>().Subject;
        users.Should().HaveCount(3);
        users.Should().Contain(u => u.UserName == "user1");
        users.Should().Contain(u => u.UserName == "user2");
        users.Should().Contain(u => u.UserName == "user3");
    }

    #endregion

    #region GetUserById Tests

    [Fact]
    public async Task GetUserById_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Act
        var result = await _controller.GetUserById(Guid.NewGuid());

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetUserById_ReturnsUser_WhenUserExists()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
        _context.Users.Add(testUser);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetUserById(testUser.Id);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var user = okResult.Value.Should().BeOfType<User>().Subject;
        user.Id.Should().Be(testUser.Id);
        user.UserName.Should().Be("testuser");
        user.Email.Should().Be("test@example.com");
        user.FullName.Should().Be("Test User");
        user.IsActive.Should().BeTrue();
    }

    #endregion

    #region CreateUser Tests

    [Fact]
    public async Task CreateUser_CreatesNewUser_AndReturnsCreatedResult()
    {
        // Arrange
        var newUser = CreateValidUser("newuser", "newuser@example.com", "New User");

        // Act
        var result = await _controller.CreateUser(newUser);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(UsersController.GetUserById));
        
        var returnedUser = createdResult.Value.Should().BeOfType<User>().Subject;
        returnedUser.UserName.Should().Be("newuser");
        returnedUser.Email.Should().Be("newuser@example.com");
        returnedUser.FullName.Should().Be("New User");
        returnedUser.IsActive.Should().BeTrue();
        returnedUser.Id.Should().NotBe(Guid.Empty);

        // Verify user was actually saved to database
        var savedUser = await _context.Users.FindAsync(returnedUser.Id);
        savedUser.Should().NotBeNull();
        savedUser!.UserName.Should().Be("newuser");
    }

    [Fact]
    public async Task CreateUser_SetsCreatedAtTimestamp()
    {
        // Arrange
        var newUser = CreateValidUser("newuser", "newuser@example.com", "New User");

        // Act
        var result = await _controller.CreateUser(newUser);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var returnedUser = createdResult.Value.Should().BeOfType<User>().Subject;
        returnedUser.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateUser_ReturnsBadRequest_WhenFullNameIsEmpty()
    {
        // Arrange
        var newUser = new User
        {
            UserName = "testuser",
            Email = "test@example.com",
            FullName = "", // Invalid - required field
            IsActive = true
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
            UserName = "testuser",
            Email = "notanemail", // Invalid format
            FullName = "Test User",
            IsActive = true
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
            UserName = "", // Invalid
            Email = "invalid", // Invalid
            FullName = "", // Invalid
            IsActive = true
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
        errors.Should().Contain(e => e.Contains("Full name"));
        errors.Should().Contain(e => e.Contains("Email"));
        errors.Should().Contain(e => e.Contains("Username"));
    }

    #endregion

    #region UpdateUser Tests

    [Fact]
    public async Task UpdateUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var updatedUser = CreateValidUser("updated", "updated@example.com", "Updated Name");

        // Act
        var result = await _controller.UpdateUser(Guid.NewGuid(), updatedUser);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateUser_UpdatesUser_WhenUserExists()
    {
        // Arrange
        var existingUser = CreateValidUser("original", "original@example.com", "Original Name");
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var updatedData = CreateValidUser("updated", "updated@example.com", "Updated Name");
        updatedData.IsActive = false;

        // Act
        var result = await _controller.UpdateUser(existingUser.Id, updatedData);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedUser = okResult.Value.Should().BeOfType<User>().Subject;
        
        returnedUser.Id.Should().Be(existingUser.Id);
        returnedUser.UserName.Should().Be("updated");
        returnedUser.Email.Should().Be("updated@example.com");
        returnedUser.FullName.Should().Be("Updated Name");
        returnedUser.IsActive.Should().BeFalse();
        returnedUser.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UpdateUser_PersistsChangesToDatabase()
    {
        // Arrange
        var existingUser = CreateValidUser("original", "original@example.com", "Original Name");
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var updatedData = CreateValidUser("updated", "updated@example.com", "Updated Name");

        // Act
        await _controller.UpdateUser(existingUser.Id, updatedData);

        // Assert
        var savedUser = await _context.Users.FindAsync(existingUser.Id);
        savedUser.Should().NotBeNull();
        savedUser!.UserName.Should().Be("updated");
        savedUser.Email.Should().Be("updated@example.com");
        savedUser.FullName.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateUser_ReturnsBadRequest_WhenValidationFails()
    {
        // Arrange
        var existingUser = CreateValidUser("original", "original@example.com", "Original Name");
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var invalidUpdate = new User
        {
            UserName = "", // Invalid
            Email = "notanemail", // Invalid
            FullName = "", // Invalid
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
    public async Task DeleteUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Act
        var result = await _controller.DeleteUser(Guid.NewGuid());

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteUser_ReturnsNoContent_WhenUserExists()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
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
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
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
        var user1 = CreateValidUser("user1", "user1@test.com", "User One");
        var user2 = CreateValidUser("user2", "user2@test.com", "User Two");
        _context.Users.AddRange(user1, user2);
        await _context.SaveChangesAsync();

        // Act
        await _controller.DeleteUser(user1.Id);

        // Assert
        var deletedUser = await _context.Users.FindAsync(user1.Id);
        var remainingUser = await _context.Users.FindAsync(user2.Id);
        
        deletedUser.Should().BeNull();
        remainingUser.Should().NotBeNull();
        remainingUser!.UserName.Should().Be("user2");
    }

    #endregion
}
