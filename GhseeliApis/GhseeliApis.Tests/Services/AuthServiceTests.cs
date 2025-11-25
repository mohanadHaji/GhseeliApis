using FluentAssertions;
using GhseeliApis.DTOs.Auth;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace GhseeliApis.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<SignInManager<User>> _signInManagerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<IAppLogger> _loggerMock;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        // Mock UserManager
        var userStoreMock = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(
            userStoreMock.Object,
            null, null, null, null, null, null, null, null);

        // Mock SignInManager
        var contextAccessorMock = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<User>>();
        _signInManagerMock = new Mock<SignInManager<User>>(
            _userManagerMock.Object,
            contextAccessorMock.Object,
            claimsFactoryMock.Object,
            null, null, null, null);

        // Mock Configuration
        _configurationMock = new Mock<IConfiguration>();
        var jwtSettingsSection = new Mock<IConfigurationSection>();
        
        _configurationMock.Setup(c => c.GetSection("JwtSettings")).Returns(jwtSettingsSection.Object);
        jwtSettingsSection.Setup(s => s["SecretKey"]).Returns("TestSecretKey_Minimum32CharactersLong_ForHmacSha256");
        jwtSettingsSection.Setup(s => s["Issuer"]).Returns("TestIssuer");
        jwtSettingsSection.Setup(s => s["Audience"]).Returns("TestAudience");
        jwtSettingsSection.Setup(s => s["ExpirationMinutes"]).Returns("60");

        _loggerMock = new Mock<IAppLogger>();

        _authService = new AuthService(
            _userManagerMock.Object,
            _signInManagerMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    #region RegisterAsync Tests

    [Fact]
    public async Task RegisterAsync_WithValidData_ShouldCreateUserAndReturnAuthResponse()
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

        _userManagerMock.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), request.Password))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new User { Id = Guid.Parse(id) });

        _userManagerMock.Setup(x => x.GetRolesAsync(It.IsAny<User>()))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.Email.Should().Be(request.Email);
        result.FullName.Should().Be(request.FullName);
        result.Token.Should().NotBeEmpty();
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        _userManagerMock.Verify(x => x.CreateAsync(
            It.Is<User>(u => u.Email == request.Email && u.FullName == request.FullName),
            request.Password), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ShouldReturnNull()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "existing@example.com",
            Password = "Test123!",
            ConfirmPassword = "Test123!",
            FullName = "Test User"
        };

        var existingUser = new User { Email = request.Email };
        _userManagerMock.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(existingUser);

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        result.Should().BeNull();
        _userManagerMock.Verify(x => x.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("already exists"))), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WhenUserCreationFails_ShouldReturnNull()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "test@example.com",
            Password = "Test123!",
            ConfirmPassword = "Test123!",
            FullName = "Test User"
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        var identityError = new IdentityError { Description = "Password too weak" };
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), request.Password))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        result.Should().BeNull();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("Registration failed"))), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_ShouldSetUserAsActiveAndCreatedAt()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "test@example.com",
            Password = "Test123!",
            ConfirmPassword = "Test123!",
            FullName = "Test User"
        };

        User? capturedUser = null;
        _userManagerMock.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), request.Password))
            .Callback<User, string>((user, _) => capturedUser = user)
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new User { Id = Guid.Parse(id) });

        _userManagerMock.Setup(x => x.GetRolesAsync(It.IsAny<User>()))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        await _authService.RegisterAsync(request);

        // Assert
        capturedUser.Should().NotBeNull();
        capturedUser!.IsActive.Should().BeTrue();
        capturedUser.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region LoginAsync Tests

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnAuthResponse()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "test@example.com",
            Password = "Test123!"
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            FullName = "Test User",
            IsActive = true
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, request.Password, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(user.Id);
        result.Email.Should().Be(user.Email);
        result.FullName.Should().Be(user.FullName);
        result.Token.Should().NotBeEmpty();
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentUser_ShouldReturnNull()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "nonexistent@example.com",
            Password = "Test123!"
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        result.Should().BeNull();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("not found"))), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithInactiveUser_ShouldReturnNull()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "inactive@example.com",
            Password = "Test123!"
        };

        var user = new User
        {
            Email = request.Email,
            IsActive = false
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        result.Should().BeNull();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("inactive"))), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithInvalidPassword_ShouldReturnNull()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "test@example.com",
            Password = "WrongPassword"
        };

        var user = new User
        {
            Email = request.Email,
            FullName = "Test User",
            IsActive = true
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, request.Password, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        result.Should().BeNull();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("Invalid password"))), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithLockedOutUser_ShouldReturnNull()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "lockedout@example.com",
            Password = "Test123!"
        };

        var user = new User
        {
            Email = request.Email,
            IsActive = true
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, request.Password, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.LockedOut);

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        result.Should().BeNull();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("locked out"))), Times.Once);
    }

    #endregion

    #region GenerateJwtToken Tests

    [Fact]
    public async Task GenerateJwtTokenAsync_ShouldReturnValidToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";
        var fullName = "Test User";
        var user = new User { Id = userId, Email = email, FullName = fullName };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var token = await _authService.GenerateJwtTokenAsync(userId, email, fullName);

        // Assert
        token.Should().NotBeEmpty();

        // Validate token structure
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadJwtToken(token);

        jwtToken.Should().NotBeNull();
        jwtToken.Claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == userId.ToString());
        jwtToken.Claims.Should().Contain(c => c.Type == ClaimTypes.Email && c.Value == email);
        jwtToken.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == fullName);
        jwtToken.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "User");
    }

    [Fact]
    public async Task GenerateJwtTokenAsync_ShouldIncludeCorrectIssuerAndAudience()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";
        var fullName = "Test User";
        var user = new User { Id = userId };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var token = await _authService.GenerateJwtTokenAsync(userId, email, fullName);

        // Assert
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadJwtToken(token);

        jwtToken.Issuer.Should().Be("TestIssuer");
        jwtToken.Audiences.Should().Contain("TestAudience");
    }

    [Fact]
    public async Task GenerateJwtTokenAsync_ShouldSetCorrectExpiration()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";
        var fullName = "Test User";
        var user = new User { Id = userId };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var token = await _authService.GenerateJwtTokenAsync(userId, email, fullName);

        // Assert
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadJwtToken(token);

        var expectedExpiration = DateTime.UtcNow.AddMinutes(60);
        jwtToken.ValidTo.Should().BeCloseTo(expectedExpiration, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GenerateJwtTokenAsync_ShouldIncludeJtiClaim()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";
        var fullName = "Test User";
        var user = new User { Id = userId };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        // Act
        var token = await _authService.GenerateJwtTokenAsync(userId, email, fullName);

        // Assert
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadJwtToken(token);

        var jtiClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti);
        jtiClaim.Should().NotBeNull();
        Guid.TryParse(jtiClaim!.Value, out _).Should().BeTrue();
    }

    #endregion

    #region ValidateTokenAsync Tests

    [Fact]
    public async Task ValidateTokenAsync_WithValidToken_ShouldReturnTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";
        var fullName = "Test User";
        var user = new User { Id = userId };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        var token = await _authService.GenerateJwtTokenAsync(userId, email, fullName);

        // Act
        var result = await _authService.ValidateTokenAsync(token);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateTokenAsync_WithInvalidToken_ShouldReturnFalse()
    {
        // Arrange
        var invalidToken = "invalid.token.here";

        // Act
        var result = await _authService.ValidateTokenAsync(invalidToken);

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("Token validation failed"))), Times.Once);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithExpiredToken_ShouldReturnFalse()
    {
        // Arrange
        // Create a configuration with 0 minute expiration
        var expiredConfigMock = new Mock<IConfiguration>();
        var jwtSettingsSection = new Mock<IConfigurationSection>();
        
        expiredConfigMock.Setup(c => c.GetSection("JwtSettings")).Returns(jwtSettingsSection.Object);
        jwtSettingsSection.Setup(s => s["SecretKey"]).Returns("TestSecretKey_Minimum32CharactersLong_ForHmacSha256");
        jwtSettingsSection.Setup(s => s["Issuer"]).Returns("TestIssuer");
        jwtSettingsSection.Setup(s => s["Audience"]).Returns("TestAudience");
        jwtSettingsSection.Setup(s => s["ExpirationMinutes"]).Returns("0"); // Immediate expiration

        var authServiceWithExpiration = new AuthService(
            _userManagerMock.Object,
            _signInManagerMock.Object,
            expiredConfigMock.Object,
            _loggerMock.Object);

        var userId = Guid.NewGuid();
        var user = new User { Id = userId };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User" });

        var token = await authServiceWithExpiration.GenerateJwtTokenAsync(userId, "test@example.com", "Test User");

        // Wait a moment to ensure token is expired
        await Task.Delay(100);

        // Act
        var result = await _authService.ValidateTokenAsync(token);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTokenAsync_WithEmptyToken_ShouldReturnFalse()
    {
        // Arrange
        var emptyToken = string.Empty;

        // Act
        var result = await _authService.ValidateTokenAsync(emptyToken);

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}
