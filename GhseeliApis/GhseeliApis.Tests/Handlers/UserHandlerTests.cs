using FluentAssertions;
using GhseeliApis.DTOs.User;
using GhseeliApis.Handlers;
using GhseeliApis.Logger;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GhseeliApis.Tests.Handlers;

/// <summary>
/// Unit tests for UserHandler with DTO-based approach
/// </summary>
public class UserHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IAppLogger _logger;
    private readonly IUserRepository _repository;
    private readonly Mock<UserManager<User>> _mockUserManager;
    private readonly UserHandler _handler;

    public UserHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _logger = new ConsoleLogger();
        _repository = new UserRepository(_context);
        
        // Create mock UserManager
        var store = new Mock<IUserStore<User>>();
        _mockUserManager = new Mock<UserManager<User>>(
            store.Object, null, null, null, null, null, null, null, null);
        
        _handler = new UserHandler(_repository, _mockUserManager.Object, _logger);
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

        // Mock GetRolesAsync for each user
        _mockUserManager.Setup(um => um.GetRolesAsync(It.IsAny<User>()))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var users = await _handler.GetAllUsersAsync();

        // Assert
        users.Should().HaveCount(3);
        users.Should().Contain(u => u.Email == "user1@test.com");
        users.Should().Contain(u => u.Email == "user2@test.com");
        users.Should().Contain(u => u.Email == "user3@test.com");
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

        // Mock GetRolesAsync
        _mockUserManager.Setup(um => um.GetRolesAsync(It.IsAny<User>()))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var user = await _handler.GetUserByIdAsync(testUser.Id);

        // Assert
        user.Should().NotBeNull();
        user!.Id.Should().Be(testUser.Id);
        user.Email.Should().Be("test@example.com");
        user.FullName.Should().Be("Test User");
        user.Roles.Should().Contain("User");
    }

    #endregion

    #region CreateUserAsync Tests

    [Fact]
    public async Task CreateUserAsync_CreatesUserInDatabase()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Email = "newuser@example.com",
            FullName = "New User",
            Phone = "1234567890",
            Password = "Password123!",
            Role = "User"
        };

        var createdUser = new User
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            Phone = request.Phone,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _mockUserManager.Setup(um => um.CreateAsync(It.IsAny<User>(), request.Password))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<User, string>((u, p) => u.Id = createdUser.Id);

        _mockUserManager.Setup(um => um.AddToRoleAsync(It.IsAny<User>(), request.Role))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(um => um.GetRolesAsync(It.IsAny<User>()))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var result = await _handler.CreateUserAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Email.Should().Be("newuser@example.com");
        result.FullName.Should().Be("New User");
        result.Roles.Should().Contain("User");
    }

    [Fact]
    public async Task CreateUserAsync_ReturnsUserWithGeneratedId()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Email = "newuser@example.com",
            FullName = "New User",
            Password = "Password123!"
        };

        var userId = Guid.NewGuid();
        _mockUserManager.Setup(um => um.CreateAsync(It.IsAny<User>(), request.Password))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<User, string>((u, p) => u.Id = userId);

        _mockUserManager.Setup(um => um.AddToRoleAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(um => um.GetRolesAsync(It.IsAny<User>()))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var result = await _handler.CreateUserAsync(request);

        // Assert
        result.Id.Should().NotBe(Guid.Empty);
    }

    #endregion

    #region UpdateUserAsync Tests

    [Fact]
    public async Task UpdateUserAsync_ReturnsNull_WhenUserDoesNotExist()
    {
        // Arrange
        var request = new UpdateUserRequest
        {
            Email = "updated@example.com",
            FullName = "Updated User"
        };

        _mockUserManager.Setup(um => um.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((User)null!);

        // Act
        var result = await _handler.UpdateUserAsync(Guid.NewGuid(), request);

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

        var request = new UpdateUserRequest
        {
            Email = "updated@example.com",
            FullName = "Updated Name",
            IsActive = false
        };

        _mockUserManager.Setup(um => um.FindByIdAsync(existingUser.Id.ToString()))
            .ReturnsAsync(existingUser);

        _mockUserManager.Setup(um => um.UpdateAsync(existingUser))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<User>((u) => 
            {
                u.FullName = request.FullName ?? u.FullName;
                u.IsActive = request.IsActive ?? u.IsActive;
            });

        _mockUserManager.Setup(um => um.GetRolesAsync(existingUser))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var result = await _handler.UpdateUserAsync(existingUser.Id, request);

        // Assert
        result.Should().NotBeNull();
        result!.Email.Should().Be("original@example.com"); // Email unchanged - goes to PendingEmail
        result.PendingEmail.Should().Be("updated@example.com");
        result.FullName.Should().Be("Updated Name");
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateUserAsync_PersistsChangesToDatabase()
    {
        // Arrange
        var existingUser = CreateValidUser("original", "original@example.com", "Original Name");
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var request = new UpdateUserRequest
        {
            FullName = "Updated Name"
        };

        _mockUserManager.Setup(um => um.FindByIdAsync(existingUser.Id.ToString()))
            .ReturnsAsync(existingUser);

        _mockUserManager.Setup(um => um.UpdateAsync(existingUser))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(um => um.GetRolesAsync(existingUser))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        await _handler.UpdateUserAsync(existingUser.Id, request);

        // Assert
        _mockUserManager.Verify(um => um.UpdateAsync(existingUser), Times.Once);
    }

    #endregion

    #region DeleteUserAsync Tests

    [Fact]
    public async Task DeleteUserAsync_ReturnsFalse_WhenUserDoesNotExist()
    {
        // Arrange
        _mockUserManager.Setup(um => um.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((User)null!);

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
        
        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        _mockUserManager.Setup(um => um.DeleteAsync(testUser))
            .ReturnsAsync(IdentityResult.Success);

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
        var userId = testUser.Id;

        _mockUserManager.Setup(um => um.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(testUser);

        _mockUserManager.Setup(um => um.DeleteAsync(testUser))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _handler.DeleteUserAsync(userId);

        // Assert
        _mockUserManager.Verify(um => um.DeleteAsync(testUser), Times.Once);
    }

    [Fact]
    public async Task DeleteUserAsync_OnlyDeletesSpecifiedUser()
    {
        // Arrange
        var user1 = CreateValidUser("user1", "user1@test.com", "User One");

        _mockUserManager.Setup(um => um.FindByIdAsync(user1.Id.ToString()))
            .ReturnsAsync(user1);

        _mockUserManager.Setup(um => um.DeleteAsync(user1))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        await _handler.DeleteUserAsync(user1.Id);

        // Assert
        _mockUserManager.Verify(um => um.DeleteAsync(user1), Times.Once);
        _mockUserManager.Verify(um => um.DeleteAsync(It.Is<User>(u => u.Id != user1.Id)), Times.Never);
    }

    #endregion

    #region ChangePasswordAsync Tests

    [Fact]
    public async Task ChangePasswordAsync_ReturnsFalse_WhenUserDoesNotExist()
    {
        // Arrange
        _mockUserManager.Setup(um => um.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((User)null!);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "OldPass123!",
            NewPassword = "NewPass123!",
            ConfirmNewPassword = "NewPass123!"
        };

        // Act
        var result = await _handler.ChangePasswordAsync(Guid.NewGuid(), request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePasswordAsync_ThrowsInvalidOperationException_WhenChangePasswordFails()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        _mockUserManager.Setup(um => um.ChangePasswordAsync(testUser, "WrongPass!", "NewPass123!"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Incorrect password." }));

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "WrongPass!",
            NewPassword = "NewPass123!",
            ConfirmNewPassword = "NewPass123!"
        };

        // Act
        var act = () => _handler.ChangePasswordAsync(testUser.Id, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Incorrect password*");
    }

    [Fact]
    public async Task ChangePasswordAsync_ReturnsTrue_WhenPasswordChangedSuccessfully()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        _mockUserManager.Setup(um => um.ChangePasswordAsync(testUser, "OldPass123!", "NewPass123!"))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(um => um.UpdateAsync(testUser))
            .ReturnsAsync(IdentityResult.Success);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "OldPass123!",
            NewPassword = "NewPass123!",
            ConfirmNewPassword = "NewPass123!"
        };

        // Act
        var result = await _handler.ChangePasswordAsync(testUser.Id, request);

        // Assert
        result.Should().BeTrue();
        testUser.UpdatedAt.Should().NotBeNull();
        _mockUserManager.Verify(um => um.UpdateAsync(testUser), Times.Once);
    }

    #endregion

    #region SoftDeleteUserAsync Tests

    [Fact]
    public async Task SoftDeleteUserAsync_ReturnsFalse_WhenUserDoesNotExist()
    {
        // Arrange
        _mockUserManager.Setup(um => um.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((User)null!);

        // Act
        var result = await _handler.SoftDeleteUserAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SoftDeleteUserAsync_ReturnsTrue_AndSetsFieldsCorrectly()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        _mockUserManager.Setup(um => um.UpdateAsync(testUser))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _handler.SoftDeleteUserAsync(testUser.Id);

        // Assert
        result.Should().BeTrue();
        testUser.IsActive.Should().BeFalse();
        testUser.DeleteScheduledFor.Should().NotBeNull();
        testUser.DeleteScheduledFor.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromSeconds(5));
        testUser.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SoftDeleteUserAsync_ThrowsInvalidOperationException_WhenUpdateFails()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        _mockUserManager.Setup(um => um.UpdateAsync(testUser))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Update failed" }));

        // Act
        var act = () => _handler.SoftDeleteUserAsync(testUser.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Update failed*");
    }

    #endregion

    #region ReactivateAccountAsync Tests

    [Fact]
    public async Task ReactivateAccountAsync_ReturnsFalse_WhenUserDoesNotExist()
    {
        // Arrange
        _mockUserManager.Setup(um => um.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((User)null!);

        // Act
        var result = await _handler.ReactivateAccountAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReactivateAccountAsync_ThrowsInvalidOperationException_WhenAlreadyActive()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
        testUser.IsActive = true;
        testUser.DeleteScheduledFor = null;

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        // Act
        var act = () => _handler.ReactivateAccountAsync(testUser.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already active*");
    }

    [Fact]
    public async Task ReactivateAccountAsync_ReturnsTrue_WhenSoftDeletedUserIsReactivated()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
        testUser.IsActive = false;
        testUser.DeleteScheduledFor = DateTime.UtcNow.AddDays(25);

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        _mockUserManager.Setup(um => um.UpdateAsync(testUser))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _handler.ReactivateAccountAsync(testUser.Id);

        // Assert
        result.Should().BeTrue();
        testUser.IsActive.Should().BeTrue();
        testUser.DeleteScheduledFor.Should().BeNull();
        testUser.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReactivateAccountAsync_ThrowsInvalidOperationException_WhenUpdateFails()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
        testUser.IsActive = false;
        testUser.DeleteScheduledFor = DateTime.UtcNow.AddDays(20);

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        _mockUserManager.Setup(um => um.UpdateAsync(testUser))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Update failed" }));

        // Act
        var act = () => _handler.ReactivateAccountAsync(testUser.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Update failed*");
    }

    #endregion

    #region GenerateEmailChangeTokenAsync Tests

    [Fact]
    public async Task GenerateEmailChangeTokenAsync_ThrowsInvalidOperationException_WhenUserNotFound()
    {
        // Arrange
        _mockUserManager.Setup(um => um.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((User)null!);

        // Act
        var act = () => _handler.GenerateEmailChangeTokenAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GenerateEmailChangeTokenAsync_ThrowsInvalidOperationException_WhenNoPendingEmail()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
        testUser.PendingEmail = null;

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        // Act
        var act = () => _handler.GenerateEmailChangeTokenAsync(testUser.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*pending email*");
    }

    [Fact]
    public async Task GenerateEmailChangeTokenAsync_ReturnsToken_WhenPendingEmailExists()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
        testUser.PendingEmail = "newemail@example.com";

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        _mockUserManager.Setup(um => um.GenerateChangeEmailTokenAsync(testUser, "newemail@example.com"))
            .ReturnsAsync("test-token-123");

        // Act
        var token = await _handler.GenerateEmailChangeTokenAsync(testUser.Id);

        // Assert
        token.Should().Be("test-token-123");
    }

    #endregion

    #region ConfirmEmailChangeAsync Tests

    [Fact]
    public async Task ConfirmEmailChangeAsync_ReturnsFalse_WhenUserDoesNotExist()
    {
        // Arrange
        _mockUserManager.Setup(um => um.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((User)null!);

        // Act
        var result = await _handler.ConfirmEmailChangeAsync(Guid.NewGuid(), "some-token");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_ThrowsInvalidOperationException_WhenNoPendingEmail()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
        testUser.PendingEmail = null;

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        // Act
        var act = () => _handler.ConfirmEmailChangeAsync(testUser.Id, "some-token");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*pending email*");
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_ThrowsInvalidOperationException_WhenChangeEmailFails()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
        testUser.PendingEmail = "newemail@example.com";

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        _mockUserManager.Setup(um => um.ChangeEmailAsync(testUser, "newemail@example.com", "bad-token"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Invalid token" }));

        // Act
        var act = () => _handler.ConfirmEmailChangeAsync(testUser.Id, "bad-token");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid token*");
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_ReturnsTrue_AndUpdatesUserFields()
    {
        // Arrange
        var testUser = CreateValidUser("testuser", "test@example.com", "Test User");
        testUser.PendingEmail = "newemail@example.com";

        _mockUserManager.Setup(um => um.FindByIdAsync(testUser.Id.ToString()))
            .ReturnsAsync(testUser);

        _mockUserManager.Setup(um => um.ChangeEmailAsync(testUser, "newemail@example.com", "valid-token"))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(um => um.UpdateAsync(testUser))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _handler.ConfirmEmailChangeAsync(testUser.Id, "valid-token");

        // Assert
        result.Should().BeTrue();
        testUser.UserName.Should().Be("newemail@example.com");
        testUser.PendingEmail.Should().BeNull();
        testUser.UpdatedAt.Should().NotBeNull();
        _mockUserManager.Verify(um => um.UpdateAsync(testUser), Times.Once);
    }

    #endregion
}
