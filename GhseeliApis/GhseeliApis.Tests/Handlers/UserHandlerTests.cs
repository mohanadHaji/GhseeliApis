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
            CreateValidUser("user1", "user1@test.com", "User One"),
            CreateValidUser("user2", "user2@test.com", "User Two"),
            CreateValidUser("user3", "user3@test.com", "User Three")
        };
        _context.Users.AddRange(testUsers);
        await _context.SaveChangesAsync();

        // Act
        var users = await _handler.GetAllUsersAsync();

        // Assert
        users.Should().HaveCount(3);
        users.Should().Contain(u => u.UserName == "user1");
        users.Should().Contain(u => u.UserName == "user2");
        users.Should().Contain(u => u.UserName == "user3");
    }

    #endregion

    #region GetUserByIdAsync Tests

    [Fact]
    public async Task GetUserByIdAsync_ReturnsNull_WhenUserDoesNotExist()
    {
        // Act
        var user = await _handler.GetUserByIdAsync(Guid.NewGuid());

        // Assert
        user.Should().BeNull();
    }

    [Fact]
    public async Task GetUserByIdAsync_ReturnsUser_WhenUserExists()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
        _context.Users.Add(testUser);
        await _context.SaveChangesAsync();

        // Act
        var user = await _handler.GetUserByIdAsync(testUser.Id);

        // Assert
        user.Should().NotBeNull();
        user!.Id.Should().Be(testUser.Id);
        user.UserName.Should().Be("testuser");
        user.Email.Should().Be("test@example.com");
        user.FullName.Should().Be("Test User");
    }

    #endregion

    #region CreateUserAsync Tests

    [Fact]
    public async Task CreateUserAsync_CreatesUserInDatabase()
    {
        // Arrange
        var newUser = CreateValidUser("newuser", "newuser@example.com", "New User");

        // Act
        var createdUser = await _handler.CreateUserAsync(newUser);

        // Assert
        createdUser.Should().NotBeNull();
        createdUser.Id.Should().NotBe(Guid.Empty);
        createdUser.UserName.Should().Be("newuser");
        createdUser.FullName.Should().Be("New User");

        // Verify it's in the database
        var dbUser = await _context.Users.FindAsync(createdUser.Id);
        dbUser.Should().NotBeNull();
        dbUser!.UserName.Should().Be("newuser");
    }

    [Fact]
    public async Task CreateUserAsync_ReturnsUserWithGeneratedId()
    {
        // Arrange
        var newUser = CreateValidUser("newuser", "newuser@example.com", "New User");

        // Act
        var createdUser = await _handler.CreateUserAsync(newUser);

        // Assert
        createdUser.Id.Should().NotBe(Guid.Empty);
    }

    #endregion

    #region UpdateUserAsync Tests

    [Fact]
    public async Task UpdateUserAsync_ReturnsNull_WhenUserDoesNotExist()
    {
        // Arrange
        var updateData = CreateValidUser("updated", "updated@example.com", "Updated User");

        // Act
        var result = await _handler.UpdateUserAsync(Guid.NewGuid(), updateData);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateUserAsync_UpdatesUser_WhenUserExists()
    {
        // Arrange
        var existingUser = CreateValidUser("original", "original@example.com", "Original Name");
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var updateData = CreateValidUser("updated", "updated@example.com", "Updated Name");
        updateData.IsActive = false;

        // Act
        var result = await _handler.UpdateUserAsync(existingUser.Id, updateData);

        // Assert
        result.Should().NotBeNull();
        result!.UserName.Should().Be("updated");
        result.Email.Should().Be("updated@example.com");
        result.FullName.Should().Be("Updated Name");
        result.IsActive.Should().BeFalse();
        result.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateUserAsync_PersistsChangesToDatabase()
    {
        // Arrange
        var existingUser = CreateValidUser("original", "original@example.com", "Original Name");
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var updateData = CreateValidUser("updated", "updated@example.com", "Updated Name");

        // Act
        await _handler.UpdateUserAsync(existingUser.Id, updateData);

        // Assert
        var dbUser = await _context.Users.FindAsync(existingUser.Id);
        dbUser.Should().NotBeNull();
        dbUser!.UserName.Should().Be("updated");
        dbUser.Email.Should().Be("updated@example.com");
        dbUser.FullName.Should().Be("Updated Name");
    }

    #endregion

    #region DeleteUserAsync Tests

    [Fact]
    public async Task DeleteUserAsync_ReturnsFalse_WhenUserDoesNotExist()
    {
        // Act
        var result = await _handler.DeleteUserAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsTrue_WhenUserExists()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
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
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
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
        var user1 = CreateValidUser("user1", "user1@test.com", "User One");
        var user2 = CreateValidUser("user2", "user2@test.com", "User Two");
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
