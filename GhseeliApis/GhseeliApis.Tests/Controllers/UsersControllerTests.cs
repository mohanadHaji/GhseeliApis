using FluentAssertions;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.User;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger;
using GhseeliApis.Logger.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Unit tests for UsersController with DTOs
/// </summary>
public class UsersControllerTests
{
    private readonly Mock<IUserHandler> _mockUserHandler;
    private readonly IAppLogger _logger;
    private readonly UsersController _controller;

    public UsersControllerTests()
    {
        _mockUserHandler = new Mock<IUserHandler>();
        _logger = new ConsoleLogger();
        _controller = new UsersController(_mockUserHandler.Object, _logger);
    }

    #region GetAllUsers Tests

    [Fact]
    public async Task GetAllUsers_ReturnsEmptyList_WhenNoUsersExist()
    {
        // Arrange
        _mockUserHandler.Setup(h => h.GetAllUsersAsync())
            .ReturnsAsync(new List<UserListResponse>());

        // Act
        var result = await _controller.GetAllUsers();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var users = okResult.Value.Should().BeAssignableTo<List<UserListResponse>>().Subject;
        users.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllUsers_ReturnsAllUsers_WhenUsersExist()
    {
        // Arrange
        var testUsers = new List<UserListResponse>
        {
            new() { Id = Guid.NewGuid(), Email = "user1@test.com", FullName = "User One", IsActive = true, CreatedAt = DateTime.UtcNow, Roles = new List<string> { "User" } },
            new() { Id = Guid.NewGuid(), Email = "user2@test.com", FullName = "User Two", IsActive = true, CreatedAt = DateTime.UtcNow, Roles = new List<string> { "User" } },
            new() { Id = Guid.NewGuid(), Email = "user3@test.com", FullName = "User Three", IsActive = true, CreatedAt = DateTime.UtcNow, Roles = new List<string> { "User" } }
        };

        _mockUserHandler.Setup(h => h.GetAllUsersAsync())
            .ReturnsAsync(testUsers);

        // Act
        var result = await _controller.GetAllUsers();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var users = okResult.Value.Should().BeAssignableTo<List<UserListResponse>>().Subject;
        users.Should().HaveCount(3);
        users.Should().Contain(u => u.Email == "user1@test.com");
        users.Should().Contain(u => u.Email == "user2@test.com");
        users.Should().Contain(u => u.Email == "user3@test.com");
    }

    #endregion

    #region GetUserById Tests

    [Fact]
    public async Task GetUserById_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockUserHandler.Setup(h => h.GetUserByIdAsync(userId))
            .ReturnsAsync((UserResponse)null!);

        // Act
        var result = await _controller.GetUserById(userId);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetUserById_ReturnsUser_WhenUserExists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var testUser = new UserResponse
        {
            Id = userId,
            Email = "test@example.com",
            FullName = "Test User",
            Phone = "1234567890",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Roles = new List<string> { "User" },
            VehicleCount = 2,
            AddressCount = 1,
            BookingCount = 5
        };

        _mockUserHandler.Setup(h => h.GetUserByIdAsync(userId))
            .ReturnsAsync(testUser);

        // Act
        var result = await _controller.GetUserById(userId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var user = okResult.Value.Should().BeOfType<UserResponse>().Subject;
        user.Id.Should().Be(userId);
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
        var request = new CreateUserRequest
        {
            Email = "newuser@example.com",
            FullName = "New User",
            Phone = "1234567890",
            Password = "Password123!",
            Role = "User"
        };

        var createdUser = new UserResponse
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            FullName = request.FullName,
            Phone = request.Phone,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Roles = new List<string> { "User" }
        };

        _mockUserHandler.Setup(h => h.CreateUserAsync(It.IsAny<CreateUserRequest>()))
            .ReturnsAsync(createdUser);

        // Act
        var result = await _controller.CreateUser(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be(nameof(UsersController.GetUserById));
        
        var returnedUser = createdResult.Value.Should().BeOfType<UserResponse>().Subject;
        returnedUser.Email.Should().Be("newuser@example.com");
        returnedUser.FullName.Should().Be("New User");
        returnedUser.IsActive.Should().BeTrue();
        returnedUser.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreateUser_SetsCreatedAtTimestamp()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Email = "newuser@example.com",
            FullName = "New User",
            Password = "Password123!"
        };

        var createdUser = new UserResponse
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            FullName = request.FullName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Roles = new List<string> { "User" }
        };

        _mockUserHandler.Setup(h => h.CreateUserAsync(It.IsAny<CreateUserRequest>()))
            .ReturnsAsync(createdUser);

        // Act
        var result = await _controller.CreateUser(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var returnedUser = createdResult.Value.Should().BeOfType<UserResponse>().Subject;
        returnedUser.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateUser_ReturnsBadRequest_WhenFullNameIsEmpty()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Email = "test@example.com",
            FullName = "", // Invalid - required field
            Password = "Password123!"
        };

        // Manually add validation error to ModelState
        _controller.ModelState.AddModelError("FullName", "The FullName field is required.");

        // Act
        var result = await _controller.CreateUser(request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateUser_ReturnsBadRequest_WhenEmailIsInvalid()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            Email = "notanemail", // Invalid format
            FullName = "Test User",
            Password = "Password123!"
        };

        _controller.ModelState.AddModelError("Email", "The Email field is not a valid e-mail address.");

        // Act
        var result = await _controller.CreateUser(request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateUser_ReturnsBadRequest_WhenNullRequestBody()
    {
        // Act
        var result = await _controller.CreateUser(null!);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    #endregion

    #region UpdateUser Tests

    [Fact]
    public async Task UpdateUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new UpdateUserRequest
        {
            Email = "updated@example.com",
            FullName = "Updated Name"
        };

        _mockUserHandler.Setup(h => h.UpdateUserAsync(userId, It.IsAny<UpdateUserRequest>()))
            .ReturnsAsync((UserResponse)null!);

        // Act
        var result = await _controller.UpdateUser(userId, request);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateUser_UpdatesUser_WhenUserExists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new UpdateUserRequest
        {
            Email = "updated@example.com",
            FullName = "Updated Name",
            IsActive = false
        };

        var updatedUser = new UserResponse
        {
            Id = userId,
            Email = "updated@example.com",
            FullName = "Updated Name",
            IsActive = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow,
            Roles = new List<string> { "User" }
        };

        _mockUserHandler.Setup(h => h.UpdateUserAsync(userId, It.IsAny<UpdateUserRequest>()))
            .ReturnsAsync(updatedUser);

        // Act
        var result = await _controller.UpdateUser(userId, request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedUser = okResult.Value.Should().BeOfType<UserResponse>().Subject;
        
        returnedUser.Id.Should().Be(userId);
        returnedUser.Email.Should().Be("updated@example.com");
        returnedUser.FullName.Should().Be("Updated Name");
        returnedUser.IsActive.Should().BeFalse();
        returnedUser.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UpdateUser_CallsHandler_WhenRequestIsValid()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new UpdateUserRequest
        {
            Email = "updated@example.com",
            FullName = "Updated Name"
        };

        var updatedUser = new UserResponse
        {
            Id = userId,
            Email = request.Email,
            FullName = request.FullName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Roles = new List<string> { "User" }
        };

        _mockUserHandler.Setup(h => h.UpdateUserAsync(userId, It.IsAny<UpdateUserRequest>()))
            .ReturnsAsync(updatedUser);

        // Act
        await _controller.UpdateUser(userId, request);

        // Assert
        _mockUserHandler.Verify(h => h.UpdateUserAsync(userId, It.IsAny<UpdateUserRequest>()), Times.Once);
    }

    [Fact]
    public async Task UpdateUser_ReturnsBadRequest_WhenValidationFails()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var invalidRequest = new UpdateUserRequest
        {
            Email = "notanemail" // Invalid format
        };

        _controller.ModelState.AddModelError("Email", "The Email field is not a valid e-mail address.");

        // Act
        var result = await _controller.UpdateUser(userId, invalidRequest);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    #endregion

    #region DeleteUser Tests

    [Fact]
    public async Task DeleteUser_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockUserHandler.Setup(h => h.DeleteUserAsync(userId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.DeleteUser(userId);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteUser_ReturnsNoContent_WhenUserExists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockUserHandler.Setup(h => h.DeleteUserAsync(userId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.DeleteUser(userId);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteUser_CallsHandler_WhenUserExists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockUserHandler.Setup(h => h.DeleteUserAsync(userId))
            .ReturnsAsync(true);

        // Act
        await _controller.DeleteUser(userId);

        // Assert
        _mockUserHandler.Verify(h => h.DeleteUserAsync(userId), Times.Once);
    }

    [Fact]
    public async Task DeleteUser_OnlyDeletesSpecifiedUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _mockUserHandler.Setup(h => h.DeleteUserAsync(userId))
            .ReturnsAsync(true);

        // Act
        await _controller.DeleteUser(userId);

        // Assert
        _mockUserHandler.Verify(h => h.DeleteUserAsync(userId), Times.Once);
        _mockUserHandler.Verify(h => h.DeleteUserAsync(It.Is<Guid>(id => id != userId)), Times.Never);
    }

    #endregion
}
