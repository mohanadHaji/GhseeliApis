using FluentAssertions;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Auth;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace GhseeliApis.Tests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly Mock<IAppLogger> _loggerMock;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _authServiceMock = new Mock<IAuthService>();
        _loggerMock = new Mock<IAppLogger>();
        _controller = new AuthController(_authServiceMock.Object, _loggerMock.Object);
    }

    #region Register Tests

    [Fact]
    public async Task Register_WithValidRequest_ShouldReturnOkWithAuthResponse()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "test@example.com",
            Password = "Test123!",
            ConfirmPassword = "Test123!",
            FullName = "Test User",
            PhoneNumber = "1234567890"
        };

        var authResponse = new AuthResponse
        {
            UserId = Guid.NewGuid(),
            Email = request.Email,
            FullName = request.FullName,
            Token = "jwt.token.here",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        _authServiceMock.Setup(x => x.RegisterAsync(request, It.IsAny<string>()))
            .ReturnsAsync(authResponse);

        // Act
        var result = await _controller.Register(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(authResponse);
        _authServiceMock.Verify(x => x.RegisterAsync(request, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Register_WhenServiceReturnsNull_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "existing@example.com",
            Password = "Test123!",
            ConfirmPassword = "Test123!",
            FullName = "Test User"
        };

        _authServiceMock.Setup(x => x.RegisterAsync(request, It.IsAny<string>()))
            .ReturnsAsync((AuthResponse?)null);

        // Act
        var result = await _controller.Register(request);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Register_WithInvalidModelState_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "invalid-email",
            Password = "short",
            ConfirmPassword = "different",
            FullName = ""
        };

        _controller.ModelState.AddModelError("Email", "Invalid email format");
        _controller.ModelState.AddModelError("Password", "Password too short");

        // Act
        var result = await _controller.Register(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        _authServiceMock.Verify(x => x.RegisterAsync(It.IsAny<RegisterRequest>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Register_WhenExceptionThrown_ShouldReturn500()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "test@example.com",
            Password = "Test123!",
            ConfirmPassword = "Test123!",
            FullName = "Test User"
        };

        _authServiceMock.Setup(x => x.RegisterAsync(request, It.IsAny<string>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Register(request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
        _loggerMock.Verify(x => x.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region Login Tests

    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturnOkWithAuthResponse()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "test@example.com",
            Password = "Test123!"
        };

        var authResponse = new AuthResponse
        {
            UserId = Guid.NewGuid(),
            Email = request.Email,
            FullName = "Test User",
            Token = "jwt.token.here",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        _authServiceMock.Setup(x => x.LoginAsync(request))
            .ReturnsAsync(authResponse);

        // Act
        var result = await _controller.Login(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(authResponse);
        _authServiceMock.Verify(x => x.LoginAsync(request), Times.Once);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ShouldReturnUnauthorized()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "test@example.com",
            Password = "WrongPassword"
        };

        _authServiceMock.Setup(x => x.LoginAsync(request))
            .ReturnsAsync((AuthResponse?)null);

        // Act
        var result = await _controller.Login(request);

        // Assert
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorizedResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Login_WithInvalidModelState_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "",
            Password = ""
        };

        _controller.ModelState.AddModelError("Email", "Email is required");
        _controller.ModelState.AddModelError("Password", "Password is required");

        // Act
        var result = await _controller.Login(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        _authServiceMock.Verify(x => x.LoginAsync(It.IsAny<LoginRequest>()), Times.Never);
    }

    [Fact]
    public async Task Login_WhenExceptionThrown_ShouldReturn500()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "test@example.com",
            Password = "Test123!"
        };

        _authServiceMock.Setup(x => x.LoginAsync(request))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.Login(request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
        _loggerMock.Verify(x => x.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region ValidateToken Tests

    [Fact]
    public async Task ValidateToken_WithValidToken_ShouldReturnOk()
    {
        // Arrange
        var token = "valid.jwt.token";

        _authServiceMock.Setup(x => x.ValidateTokenAsync(token))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.ValidateToken(token);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
        _authServiceMock.Verify(x => x.ValidateTokenAsync(token), Times.Once);
    }

    [Fact]
    public async Task ValidateToken_WithInvalidToken_ShouldReturnUnauthorized()
    {
        // Arrange
        var token = "invalid.jwt.token";

        _authServiceMock.Setup(x => x.ValidateTokenAsync(token))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.ValidateToken(token);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task ValidateToken_WithEmptyToken_ShouldReturnBadRequest()
    {
        // Arrange
        var token = string.Empty;

        // Act
        var result = await _controller.ValidateToken(token);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        _authServiceMock.Verify(x => x.ValidateTokenAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ValidateToken_WhenExceptionThrown_ShouldReturn500()
    {
        // Arrange
        var token = "valid.jwt.token";

        _authServiceMock.Setup(x => x.ValidateTokenAsync(token))
            .ThrowsAsync(new Exception("Validation error"));

        // Act
        var result = await _controller.ValidateToken(token);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
        _loggerMock.Verify(x => x.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion

    #region GetCurrentUser Tests

    [Fact]
    public void GetCurrentUser_WithAuthenticatedUser_ShouldReturnOkWithUserInfo()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";
        var fullName = "Test User";

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, fullName)
        };

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };

        // Act
        var result = _controller.GetCurrentUser();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = okResult.Value;
        value.Should().NotBeNull();
    }

    [Fact]
    public void GetCurrentUser_WithoutAuthenticatedUser_ShouldReturnUnauthorized()
    {
        // Arrange
        var claims = new List<Claim>();
        var identity = new ClaimsIdentity(claims);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };

        // Act
        var result = _controller.GetCurrentUser();

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public void GetCurrentUser_WhenExceptionThrown_ShouldReturn500()
    {
        // Arrange
        // Force exception by not setting up HttpContext properly
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = null! // This will cause an exception
        };

        // Act
        var result = _controller.GetCurrentUser();

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(500);
        _loggerMock.Verify(x => x.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    #endregion
}
