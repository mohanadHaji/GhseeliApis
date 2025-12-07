using FluentAssertions;
using GhseeliApis.Constants;
using GhseeliApis.DTOs.Auth;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;

namespace GhseeliApis.Tests.Services;

public class AuthServiceOAuthTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<SignInManager<User>> _signInManagerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<IAppLogger> _loggerMock;
    private readonly AuthService _authService;

    public AuthServiceOAuthTests()
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

    private ExternalLoginInfo CreateExternalLoginInfo(string provider, string email, string name, string providerKey = "123456")
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, name)
        };

        var identity = new ClaimsIdentity(claims, provider);
        var principal = new ClaimsPrincipal(identity);

        return new ExternalLoginInfo(principal, provider, providerKey, provider);
    }

    #region ExternalLoginCallbackAsync Tests

    [Fact]
    public async Task ExternalLoginCallbackAsync_WithNewUser_ShouldCreateUserAndReturnResponse()
    {
        // Arrange
        var email = "newuser@gmail.com";
        var name = "New User";
        var provider = "Google";
        var externalLoginInfo = CreateExternalLoginInfo(provider, email, name);

        _userManagerMock.Setup(x => x.FindByEmailAsync(email))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<User>(), AppRoles.User))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.AddLoginAsync(It.IsAny<User>(), externalLoginInfo))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new User { Id = Guid.Parse(id), Email = email, FullName = name });

        _userManagerMock.Setup(x => x.GetRolesAsync(It.IsAny<User>()))
            .ReturnsAsync(new List<string> { AppRoles.User });

        // Act
        var result = await _authService.ExternalLoginCallbackAsync(externalLoginInfo);

        // Assert
        result.Should().NotBeNull();
        result!.IsNewUser.Should().BeTrue();
        result.Email.Should().Be(email);
        result.FullName.Should().Be(name);
        result.Provider.Should().Be(provider);
        result.Token.Should().NotBeEmpty();
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        _userManagerMock.Verify(x => x.CreateAsync(It.Is<User>(u => 
            u.Email == email && 
            u.FullName == name && 
            u.EmailConfirmed == true &&
            u.IsActive == true)), Times.Once);

        _userManagerMock.Verify(x => x.AddToRoleAsync(It.IsAny<User>(), AppRoles.User), Times.Once);
        _userManagerMock.Verify(x => x.AddLoginAsync(It.IsAny<User>(), externalLoginInfo), Times.Once);
    }

    [Fact]
    public async Task ExternalLoginCallbackAsync_WithExistingUser_ShouldNotCreateNewUser()
    {
        // Arrange
        var email = "existing@gmail.com";
        var name = "Existing User";
        var provider = "Facebook";
        var userId = Guid.NewGuid();
        var externalLoginInfo = CreateExternalLoginInfo(provider, email, name);

        var existingUser = new User
        {
            Id = userId,
            Email = email,
            FullName = name,
            UserName = email,
            IsActive = true
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(email))
            .ReturnsAsync(existingUser);

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.AddLoginAsync(existingUser, externalLoginInfo))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(existingUser);

        _userManagerMock.Setup(x => x.GetRolesAsync(existingUser))
            .ReturnsAsync(new List<string> { AppRoles.User });

        // Act
        var result = await _authService.ExternalLoginCallbackAsync(externalLoginInfo);

        // Assert
        result.Should().NotBeNull();
        result!.IsNewUser.Should().BeFalse();
        result.UserId.Should().Be(userId);
        result.Email.Should().Be(email);
        result.Provider.Should().Be(provider);
        result.Token.Should().NotBeEmpty();

        _userManagerMock.Verify(x => x.CreateAsync(It.IsAny<User>()), Times.Never);
        _userManagerMock.Verify(x => x.AddLoginAsync(existingUser, externalLoginInfo), Times.Once);
    }

    [Fact]
    public async Task ExternalLoginCallbackAsync_WithMissingEmail_ShouldReturnNull()
    {
        // Arrange
        var provider = "Google";
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "Test User")
            // No email claim
        };

        var identity = new ClaimsIdentity(claims, provider);
        var principal = new ClaimsPrincipal(identity);
        var externalLoginInfo = new ExternalLoginInfo(principal, provider, "123456", provider);

        // Act
        var result = await _authService.ExternalLoginCallbackAsync(externalLoginInfo);

        // Assert
        result.Should().BeNull();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("No email claim found"))), Times.Once);
        _userManagerMock.Verify(x => x.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ExternalLoginCallbackAsync_WhenUserCreationFails_ShouldReturnNull()
    {
        // Arrange
        var email = "test@gmail.com";
        var name = "Test User";
        var provider = "Google";
        var externalLoginInfo = CreateExternalLoginInfo(provider, email, name);

        _userManagerMock.Setup(x => x.FindByEmailAsync(email))
            .ReturnsAsync((User?)null);

        var identityError = new IdentityError { Description = "User creation failed" };
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        // Act
        var result = await _authService.ExternalLoginCallbackAsync(externalLoginInfo);

        // Assert
        result.Should().BeNull();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("Failed to create user"))), Times.Once);
    }

    [Fact]
    public async Task ExternalLoginCallbackAsync_WithExistingLogin_ShouldNotAddLoginAgain()
    {
        // Arrange
        var email = "user@gmail.com";
        var name = "Test User";
        var provider = "Google";
        var userId = Guid.NewGuid();
        var externalLoginInfo = CreateExternalLoginInfo(provider, email, name);

        var existingUser = new User
        {
            Id = userId,
            Email = email,
            FullName = name,
            UserName = email,
            IsActive = true
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(email))
            .ReturnsAsync(existingUser);

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, It.IsAny<string>()))
            .ReturnsAsync(existingUser); // Login already linked

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(existingUser);

        _userManagerMock.Setup(x => x.GetRolesAsync(existingUser))
            .ReturnsAsync(new List<string> { AppRoles.User });

        // Act
        var result = await _authService.ExternalLoginCallbackAsync(externalLoginInfo);

        // Assert
        result.Should().NotBeNull();
        result!.Provider.Should().Be(provider);
        _userManagerMock.Verify(x => x.AddLoginAsync(It.IsAny<User>(), It.IsAny<ExternalLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task ExternalLoginCallbackAsync_WithMissingName_ShouldUseEmailPrefix()
    {
        // Arrange
        var email = "testuser@gmail.com";
        var provider = "Google";
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Email, email)
            // No name claim
        };

        var identity = new ClaimsIdentity(claims, provider);
        var principal = new ClaimsPrincipal(identity);
        var externalLoginInfo = new ExternalLoginInfo(principal, provider, "123456", provider);

        _userManagerMock.Setup(x => x.FindByEmailAsync(email))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<User>(), AppRoles.User))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.AddLoginAsync(It.IsAny<User>(), externalLoginInfo))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new User { Id = Guid.Parse(id), Email = email, FullName = "testuser" });

        _userManagerMock.Setup(x => x.GetRolesAsync(It.IsAny<User>()))
            .ReturnsAsync(new List<string> { AppRoles.User });

        // Act
        var result = await _authService.ExternalLoginCallbackAsync(externalLoginInfo);

        // Assert
        result.Should().NotBeNull();
        _userManagerMock.Verify(x => x.CreateAsync(It.Is<User>(u => u.FullName == "testuser")), Times.Once);
    }

    #endregion

    #region LinkExternalLoginAsync Tests

    [Fact]
    public async Task LinkExternalLoginAsync_WithValidUser_ShouldLinkLogin()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "user@example.com";
        var provider = "Google";
        var externalLoginInfo = CreateExternalLoginInfo(provider, email, "Test User");

        var user = new User { Id = userId, Email = email, UserName = email };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.AddLoginAsync(user, externalLoginInfo))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _authService.LinkExternalLoginAsync(userId, externalLoginInfo);

        // Assert
        result.Should().BeTrue();
        _userManagerMock.Verify(x => x.AddLoginAsync(user, externalLoginInfo), Times.Once);
        _loggerMock.Verify(x => x.LogInfo(It.Is<string>(s => s.Contains("Successfully linked"))), Times.Once);
    }

    [Fact]
    public async Task LinkExternalLoginAsync_WithNonExistentUser_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var provider = "Facebook";
        var externalLoginInfo = CreateExternalLoginInfo(provider, "test@example.com", "Test");

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _authService.LinkExternalLoginAsync(userId, externalLoginInfo);

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("not found"))), Times.Once);
        _userManagerMock.Verify(x => x.AddLoginAsync(It.IsAny<User>(), It.IsAny<ExternalLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task LinkExternalLoginAsync_WhenAlreadyLinkedToAnotherUser_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var email = "user@example.com";
        var provider = "Google";
        var externalLoginInfo = CreateExternalLoginInfo(provider, email, "Test User");

        var user = new User { Id = userId, Email = email };
        var otherUser = new User { Id = otherUserId, Email = "other@example.com" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, It.IsAny<string>()))
            .ReturnsAsync(otherUser); // Already linked to different user

        // Act
        var result = await _authService.LinkExternalLoginAsync(userId, externalLoginInfo);

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("already linked to another user"))), Times.Once);
        _userManagerMock.Verify(x => x.AddLoginAsync(It.IsAny<User>(), It.IsAny<ExternalLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task LinkExternalLoginAsync_WhenAlreadyLinkedToSameUser_ShouldReturnTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "user@example.com";
        var provider = "Facebook";
        var externalLoginInfo = CreateExternalLoginInfo(provider, email, "Test User");

        var user = new User { Id = userId, Email = email };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, It.IsAny<string>()))
            .ReturnsAsync(user); // Already linked to same user

        // Act
        var result = await _authService.LinkExternalLoginAsync(userId, externalLoginInfo);

        // Assert
        result.Should().BeTrue();
        _loggerMock.Verify(x => x.LogInfo(It.Is<string>(s => s.Contains("already linked"))), Times.Once);
        _userManagerMock.Verify(x => x.AddLoginAsync(It.IsAny<User>(), It.IsAny<ExternalLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task LinkExternalLoginAsync_WhenAddLoginFails_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "user@example.com";
        var provider = "Google";
        var externalLoginInfo = CreateExternalLoginInfo(provider, email, "Test User");

        var user = new User { Id = userId, Email = email };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.FindByLoginAsync(provider, It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        var identityError = new IdentityError { Description = "Failed to add login" };
        _userManagerMock.Setup(x => x.AddLoginAsync(user, externalLoginInfo))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        // Act
        var result = await _authService.LinkExternalLoginAsync(userId, externalLoginInfo);

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("Failed to link"))), Times.Once);
    }

    #endregion

    #region RemoveExternalLoginAsync Tests

    [Fact]
    public async Task RemoveExternalLoginAsync_WithValidLogin_ShouldRemoveLogin()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var provider = "Google";
        var providerKey = "123456";

        var user = new User { Id = userId, Email = "user@example.com" };
        var userLogin = new UserLoginInfo(provider, providerKey, provider);

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetLoginsAsync(user))
            .ReturnsAsync(new List<UserLoginInfo> { userLogin });

        _userManagerMock.Setup(x => x.RemoveLoginAsync(user, provider, providerKey))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _authService.RemoveExternalLoginAsync(userId, provider);

        // Assert
        result.Should().BeTrue();
        _userManagerMock.Verify(x => x.RemoveLoginAsync(user, provider, providerKey), Times.Once);
        _loggerMock.Verify(x => x.LogInfo(It.Is<string>(s => s.Contains("Successfully removed"))), Times.Once);
    }

    [Fact]
    public async Task RemoveExternalLoginAsync_WithNonExistentUser_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var provider = "Facebook";

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _authService.RemoveExternalLoginAsync(userId, provider);

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("not found"))), Times.Once);
        _userManagerMock.Verify(x => x.RemoveLoginAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RemoveExternalLoginAsync_WithNonExistentLogin_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var provider = "Google";

        var user = new User { Id = userId, Email = "user@example.com" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetLoginsAsync(user))
            .ReturnsAsync(new List<UserLoginInfo>()); // No logins

        // Act
        var result = await _authService.RemoveExternalLoginAsync(userId, provider);

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("not found for user"))), Times.Once);
        _userManagerMock.Verify(x => x.RemoveLoginAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RemoveExternalLoginAsync_WhenRemovalFails_ShouldReturnFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var provider = "Facebook";
        var providerKey = "789012";

        var user = new User { Id = userId, Email = "user@example.com" };
        var userLogin = new UserLoginInfo(provider, providerKey, provider);

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetLoginsAsync(user))
            .ReturnsAsync(new List<UserLoginInfo> { userLogin });

        var identityError = new IdentityError { Description = "Failed to remove login" };
        _userManagerMock.Setup(x => x.RemoveLoginAsync(user, provider, providerKey))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        // Act
        var result = await _authService.RemoveExternalLoginAsync(userId, provider);

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("Failed to remove"))), Times.Once);
    }

    #endregion

    #region GetExternalLoginsAsync Tests

    [Fact]
    public async Task GetExternalLoginsAsync_WithValidUser_ShouldReturnLogins()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "user@example.com" };

        var userLogins = new List<UserLoginInfo>
        {
            new UserLoginInfo("Google", "google123", "Google"),
            new UserLoginInfo("Facebook", "fb456", "Facebook")
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetLoginsAsync(user))
            .ReturnsAsync(userLogins);

        // Act
        var result = await _authService.GetExternalLoginsAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(2);
        result[0].LoginProvider.Should().Be("Google");
        result[0].ProviderKey.Should().Be("google123");
        result[1].LoginProvider.Should().Be("Facebook");
        result[1].ProviderKey.Should().Be("fb456");
    }

    [Fact]
    public async Task GetExternalLoginsAsync_WithNonExistentUser_ShouldReturnEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _authService.GetExternalLoginsAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        _loggerMock.Verify(x => x.LogWarning(It.Is<string>(s => s.Contains("not found"))), Times.Once);
    }

    [Fact]
    public async Task GetExternalLoginsAsync_WithNoLogins_ShouldReturnEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "user@example.com" };

        _userManagerMock.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.GetLoginsAsync(user))
            .ReturnsAsync(new List<UserLoginInfo>());

        // Act
        var result = await _authService.GetExternalLoginsAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    #endregion
}
