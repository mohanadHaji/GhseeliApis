using FluentAssertions;
using GhseeliApis.Persistence;
using GhseeliApis.Handlers;
using GhseeliApis.Logger;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Handlers;

/// <summary>
/// Unit tests for UserHandler
/// </summary>
public class UserHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IAppLogger _logger;
    private readonly IUserRepository _repository;
    private readonly UserHandler _handler;

    public UserHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _logger = new ConsoleLogger();
        _repository = new UserRepository(_context);
        _handler = new UserHandler(_repository, _logger);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    #region GetAllUsersAsync Tests

    [Fact]
    public async Task GetAllUsersAsync_ReturnsEmptyList_WhenNoUsersExist()
    {
        // Act
        var users = await _handler.GetAllUsersAsync();

        // Assert
        users.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllUsersAsync_ReturnsAllUsers_WhenUsersExist()
    {
        // Arrange
        var testUsers = new List<User>
        {
            new User { UserName = "User 1", Email = "user1@test.com" },
            new User { UserName = "User 2", Email = "user2@test.com" },
            new User { UserName = "User 3", Email = "user3@test.com" }
        };
        _context.Users.AddRange(testUsers);
        await _context.SaveChangesAsync();

        // Act
        var users = await _handler.GetAllUsersAsync();

        // Assert
        users.Should().HaveCount(3);
        users.Should().Contain(u => u.UserName == "User 1");
        users.Should().Contain(u => u.UserName == "User 2");
        users.Should().Contain(u => u.UserName == "User 3");
    }

    #endregion

    #region GetUserByIdAsync Tests

    [Fact]
    public async Task GetUserByIdAsync_ReturnsNull_WhenUserDoesNotExist()
    {
        // Act
        var user = await _handler.GetUserByIdAsync(999);

        // Assert
        user.Should().BeNull();
    }

    [Fact]
    public async Task GetUserByIdAsync_ReturnsUser_WhenUserExists()
    {
        // Arrange
        var testUser = new User
        {
            UserName = "Test User",
            Email = "test@example.com"
        };
        _context.Users.Add(testUser);
        await _context.SaveChangesAsync();

        // Act
        var user = await _handler.GetUserByIdAsync(testUser.Id);

        // Assert
        user.Should().NotBeNull();
        user!.Id.Should().Be(testUser.Id);
        user.UserName.Should().Be("Test User");
        user.Email.Should().Be("test@example.com");
    }

    #endregion

    #region CreateUserAsync Tests

    [Fact]
    public async Task CreateUserAsync_CreatesUserInDatabase()
    {
        // Arrange
        var newUser = new User
        {
            UserName = "New User",
            Email = "newuser@example.com"
        };

        // Act
        var createdUser = await _handler.CreateUserAsync(newUser);

        // Assert
        createdUser.Should().NotBeNull();
        createdUser.Id.Should().BeGreaterThan(0);
        createdUser.UserName.Should().Be("New User");

        // Verify it's in the database
        var dbUser = await _context.Users.FindAsync(createdUser.Id);
        dbUser.Should().NotBeNull();
        dbUser!.UserName.Should().Be("New User");
    }

    [Fact]
    public async Task CreateUserAsync_ReturnsUserWithGeneratedId()
    {
        // Arrange
        var newUser = new User
        {
            UserName = "New User",
            Email = "newuser@example.com"
        };

        // Act
        var createdUser = await _handler.CreateUserAsync(newUser);

        // Assert
        createdUser.Id.Should().BeGreaterThan(0);
    }

    #endregion

    #region UpdateUserAsync Tests

    [Fact]
    public async Task UpdateUserAsync_ReturnsNull_WhenUserDoesNotExist()
    {
        // Arrange
        var updateData = new User
        {
            UserName = "Updated",
            Email = "updated@example.com"
        };

        // Act
        var result = await _handler.UpdateUserAsync(999, updateData);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateUserAsync_UpdatesUser_WhenUserExists()
    {
        // Arrange
        var existingUser = new User
        {
            UserName = "Original",
            Email = "original@example.com",
            IsActive = true
        };
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var updateData = new User
        {
            UserName = "Updated",
            Email = "updated@example.com",
            IsActive = false
        };

        // Act
        var result = await _handler.UpdateUserAsync(existingUser.Id, updateData);

        // Assert
        result.Should().NotBeNull();
        result!.UserName.Should().Be("Updated");
        result.Email.Should().Be("updated@example.com");
        result.IsActive.Should().BeFalse();
        result.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateUserAsync_PersistsChangesToDatabase()
    {
        // Arrange
        var existingUser = new User
        {
            UserName = "Original",
            Email = "original@example.com"
        };
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var updateData = new User
        {
            UserName = "Updated",
            Email = "updated@example.com"
        };

        // Act
        await _handler.UpdateUserAsync(existingUser.Id, updateData);

        // Assert
        var dbUser = await _context.Users.FindAsync(existingUser.Id);
        dbUser.Should().NotBeNull();
        dbUser!.UserName.Should().Be("Updated");
        dbUser.Email.Should().Be("updated@example.com");
    }

    #endregion

    #region DeleteUserAsync Tests

    [Fact]
    public async Task DeleteUserAsync_ReturnsFalse_WhenUserDoesNotExist()
    {
        // Act
        var result = await _handler.DeleteUserAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsTrue_WhenUserExists()
    {
        // Arrange
        var testUser = new User
        {
            UserName = "Test User",
            Email = "test@example.com"
        };
        _context.Users.Add(testUser);
        await _context.SaveChangesAsync();

        // Act
        var result = await _handler.DeleteUserAsync(testUser.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteUserAsync_RemovesUserFromDatabase()
    {
        // Arrange
        var testUser = new User
        {
            UserName = "Test User",
            Email = "test@example.com"
        };
        _context.Users.Add(testUser);
        await _context.SaveChangesAsync();
        var userId = testUser.Id;

        // Act
        await _handler.DeleteUserAsync(userId);

        // Assert
        var dbUser = await _context.Users.FindAsync(userId);
        dbUser.Should().BeNull();
    }

    [Fact]
    public async Task DeleteUserAsync_OnlyDeletesSpecifiedUser()
    {
        // Arrange
        var user1 = new User { UserName = "User 1", Email = "user1@test.com" };
        var user2 = new User { UserName = "User 2", Email = "user2@test.com" };
        _context.Users.AddRange(user1, user2);
        await _context.SaveChangesAsync();

        // Act
        await _handler.DeleteUserAsync(user1.Id);

        // Assert
        var deletedUser = await _context.Users.FindAsync(user1.Id);
        var remainingUser = await _context.Users.FindAsync(user2.Id);
        
        deletedUser.Should().BeNull();
        remainingUser.Should().NotBeNull();
    }

    #endregion
}
