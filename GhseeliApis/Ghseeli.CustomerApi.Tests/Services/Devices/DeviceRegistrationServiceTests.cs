using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Devices;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using Moq;

namespace GhseeliApis.Tests.Services.Devices;

/// <summary>
/// Tests secure device token issuance and rotation behavior.
/// </summary>
public class DeviceRegistrationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly Mock<IDeviceRepository> _repository = new();
    private readonly Mock<IDeviceTokenGenerator> _tokenGenerator = new();
    private readonly Mock<IAppLogger> _logger = new();

    [Fact]
    public async Task RegisterAsync_FirstRegistration_StoresOnlyHashAndReturnsToken()
    {
        var installationId = Guid.NewGuid();
        _repository.Setup(repository => repository.GetByInstallationIdAsync(installationId, default))
            .ReturnsAsync((CustomerDevice?)null);
        var issuedToken = Token(1);
        _tokenGenerator.Setup(generator => generator.Generate()).Returns(issuedToken);
        var service = CreateService();

        var response = await service.RegisterAsync(
            new RegisterDeviceRequest
            {
                InstallationId = installationId,
                Platform = "ios",
                AppVersion = "1.2.3",
                FcmToken = " fcm-token "
            },
            null,
            null,
            default);

        response.Token.Should().Be(issuedToken);
        response.Platform.Should().Be("iOS");
        CustomerDevice saved = null!;
        _repository.Verify(repository => repository.AddAsync(
            It.Is<CustomerDevice>(device => Capture(device, out saved)),
            default));
        saved.TokenHash.Should().HaveCount(32);
        Convert.ToHexString(saved.TokenHash).Should().NotContain(response.Token);
        saved.ExpiresAt.Should().Be(Now.AddDays(90));
        saved.FcmToken.Should().Be("fcm-token");
    }

    [Fact]
    public async Task RegisterAsync_ExistingInstallationWithoutToken_ThrowsConflict()
    {
        var device = CreateDevice();
        _repository.Setup(repository => repository.GetByInstallationIdAsync(device.InstallationId, default))
            .ReturnsAsync(device);
        var service = CreateService();

        var action = () => service.RegisterAsync(
            new RegisterDeviceRequest { InstallationId = device.InstallationId, Platform = "Android" },
            null,
            null,
            default);

        await action.Should().ThrowAsync<DeviceRegistrationException>()
            .Where(exception => exception.Code == DeviceProblemCodes.RegistrationConflict);
        _repository.Verify(repository => repository.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_ValidCurrentToken_RotatesAndInvalidatesOldHash()
    {
        var oldToken = Token(2);
        var device = CreateDevice(DeviceTokenHasher.Hash(oldToken));
        var oldHash = device.TokenHash.ToArray();
        _repository.Setup(repository => repository.GetByInstallationIdAsync(device.InstallationId, default))
            .ReturnsAsync(device);
        var rotatedToken = Token(3);
        _tokenGenerator.Setup(generator => generator.Generate()).Returns(rotatedToken);
        var service = CreateService();

        var response = await service.RegisterAsync(
            new RegisterDeviceRequest
            {
                InstallationId = device.InstallationId,
                Platform = "android",
                FcmToken = "replacement-fcm"
            },
            oldToken,
            null,
            default);

        response.Token.Should().Be(rotatedToken);
        device.TokenHash.Should().NotEqual(oldHash);
        device.Platform.Should().Be("Android");
        device.FcmToken.Should().Be("replacement-fcm");
        device.ExpiresAt.Should().Be(Now.AddDays(90));
        _repository.Verify(repository => repository.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_TokenForAnotherInstallation_IsRejected()
    {
        var device = CreateDevice(DeviceTokenHasher.Hash(Token(4)));
        _repository.Setup(repository => repository.GetByInstallationIdAsync(device.InstallationId, default))
            .ReturnsAsync(device);
        var service = CreateService();

        var action = () => service.RegisterAsync(
            new RegisterDeviceRequest { InstallationId = device.InstallationId, Platform = "iOS" },
            Token(5),
            null,
            default);

        await action.Should().ThrowAsync<DeviceRegistrationException>()
            .Where(exception => exception.Code == DeviceProblemCodes.RotationUnauthorized);
    }

    [Fact]
    public async Task RegisterAsync_ExpiredCurrentToken_IsRejected()
    {
        var token = Token(6);
        var device = CreateDevice(DeviceTokenHasher.Hash(token));
        device.ExpiresAt = Now.AddSeconds(-1);
        _repository.Setup(repository => repository.GetByInstallationIdAsync(device.InstallationId, default))
            .ReturnsAsync(device);
        var service = CreateService();

        var action = () => service.RegisterAsync(
            new RegisterDeviceRequest { InstallationId = device.InstallationId, Platform = "iOS" },
            token,
            null,
            default);

        await action.Should().ThrowAsync<DeviceRegistrationException>()
            .Where(exception => exception.Code == DeviceProblemCodes.TokenExpired);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidToken_ReturnsDeviceIdentity()
    {
        var token = Token(7);
        var device = CreateDevice(DeviceTokenHasher.Hash(token));
        _repository.Setup(repository => repository.GetByTokenHashAsync(
                It.Is<byte[]>(hash => hash.SequenceEqual(device.TokenHash)),
                default))
            .ReturnsAsync(device);
        var service = CreateService();

        var result = await service.AuthenticateAsync(token, default);

        result.IsAuthenticated.Should().BeTrue();
        result.DeviceId.Should().Be(device.Id);
        result.InstallationId.Should().Be(device.InstallationId);
    }

    [Fact]
    public async Task AuthenticateAsync_ExpiredToken_ReturnsStableFailure()
    {
        var token = Token(8);
        var device = CreateDevice(DeviceTokenHasher.Hash(token));
        device.ExpiresAt = Now.AddMinutes(-1);
        _repository.Setup(repository => repository.GetByTokenHashAsync(It.IsAny<byte[]>(), default))
            .ReturnsAsync(device);
        var service = CreateService();

        var result = await service.AuthenticateAsync(token, default);

        result.IsAuthenticated.Should().BeFalse();
        result.Code.Should().Be(DeviceProblemCodes.TokenExpired);
    }

    [Fact]
    public async Task AuthenticateAsync_InactiveDevice_ReturnsStableFailure()
    {
        var token = Token(9);
        var device = CreateDevice(DeviceTokenHasher.Hash(token));
        device.IsActive = false;
        _repository.Setup(repository => repository.GetByTokenHashAsync(
                It.IsAny<byte[]>(),
                default))
            .ReturnsAsync(device);
        var service = CreateService();

        var result = await service.AuthenticateAsync(token, default);

        result.IsAuthenticated.Should().BeFalse();
        result.Code.Should().Be(DeviceProblemCodes.TokenInactive);
    }

    [Fact]
    public async Task RegisterAsync_InactiveDevice_DoesNotRotateToken()
    {
        var token = Token(10);
        var device = CreateDevice(DeviceTokenHasher.Hash(token));
        device.IsActive = false;
        _repository.Setup(repository => repository.GetByInstallationIdAsync(
                device.InstallationId,
                default))
            .ReturnsAsync(device);
        var service = CreateService();

        var action = () => service.RegisterAsync(
            new RegisterDeviceRequest
            {
                InstallationId = device.InstallationId,
                Platform = "Android"
            },
            token,
            null,
            default);

        await action.Should().ThrowAsync<DeviceRegistrationException>()
            .Where(exception => exception.Code == DeviceProblemCodes.TokenInactive);
        _repository.Verify(
            repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("ScenarioId", "STEP24-DEVICE-BIND-002")]
    public async Task RegisterAsync_AuthenticatedNewInstallation_BindsCustomer()
    {
        var installationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _repository.Setup(repository =>
                repository.GetByInstallationIdAsync(installationId, default))
            .ReturnsAsync((CustomerDevice?)null);
        _tokenGenerator.Setup(generator => generator.Generate()).Returns(Token(11));
        var service = CreateService();

        await service.RegisterAsync(
            new RegisterDeviceRequest
            {
                InstallationId = installationId,
                Platform = "Android"
            },
            null,
            userId,
            default);

        _repository.Verify(repository => repository.AddAsync(
            It.Is<CustomerDevice>(device => device.UserId == userId),
            default));
    }

    [Fact]
    [Trait("ScenarioId", "STEP24-DEVICE-LEGACY-003")]
    public async Task RegisterAsync_AuthenticatedLegacyInstallationWithoutToken_BindsAndRecovers()
    {
        var device = CreateDevice(DeviceTokenHasher.Hash(Token(12)));
        var userId = Guid.NewGuid();
        _repository.Setup(repository =>
                repository.GetByInstallationIdAsync(device.InstallationId, default))
            .ReturnsAsync(device);
        var replacement = Token(13);
        _tokenGenerator.Setup(generator => generator.Generate()).Returns(replacement);
        var service = CreateService();

        var response = await service.RegisterAsync(
            new RegisterDeviceRequest
            {
                InstallationId = device.InstallationId,
                Platform = "Android",
                FcmToken = "new-fcm"
            },
            null,
            userId,
            default);

        response.Token.Should().Be(replacement);
        device.UserId.Should().Be(userId);
        device.FcmToken.Should().Be("new-fcm");
        DeviceTokenHasher.Matches(device.TokenHash, replacement).Should().BeTrue();
        _repository.Verify(repository => repository.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    [Trait("ScenarioId", "STEP24-DEVICE-OWNER-004")]
    public async Task RegisterAsync_OwnerWithoutCurrentToken_RecoversInstallation()
    {
        var userId = Guid.NewGuid();
        var device = CreateDevice(DeviceTokenHasher.Hash(Token(14)));
        device.UserId = userId;
        device.ExpiresAt = Now.AddDays(-1);
        _repository.Setup(repository =>
                repository.GetByInstallationIdAsync(device.InstallationId, default))
            .ReturnsAsync(device);
        var replacement = Token(15);
        _tokenGenerator.Setup(generator => generator.Generate()).Returns(replacement);
        var service = CreateService();

        var response = await service.RegisterAsync(
            new RegisterDeviceRequest
            {
                InstallationId = device.InstallationId,
                Platform = "iOS"
            },
            null,
            userId,
            default);

        response.Token.Should().Be(replacement);
        device.ExpiresAt.Should().Be(Now.AddDays(90));
    }

    [Fact]
    [Trait("ScenarioId", "STEP24-DEVICE-CROSS-005")]
    public async Task RegisterAsync_DifferentAuthenticatedCustomer_IsRejectedWithoutMutation()
    {
        var device = CreateDevice(DeviceTokenHasher.Hash(Token(16)));
        device.UserId = Guid.NewGuid();
        _repository.Setup(repository =>
                repository.GetByInstallationIdAsync(device.InstallationId, default))
            .ReturnsAsync(device);
        var originalHash = device.TokenHash.ToArray();
        var service = CreateService();

        var action = () => service.RegisterAsync(
            new RegisterDeviceRequest
            {
                InstallationId = device.InstallationId,
                Platform = "Android"
            },
            null,
            Guid.NewGuid(),
            default);

        await action.Should().ThrowAsync<DeviceRegistrationException>()
            .Where(exception =>
                exception.Code == DeviceProblemCodes.OwnerConflict &&
                exception.StatusCode == StatusCodes.Status403Forbidden);
        device.TokenHash.Should().Equal(originalHash);
        _repository.Verify(repository =>
            repository.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_DifferentCustomerWithCurrentDeviceToken_CannotRebindOwner()
    {
        var currentToken = Token(20);
        var device = CreateDevice(DeviceTokenHasher.Hash(currentToken));
        device.UserId = Guid.NewGuid();
        _repository.Setup(repository =>
                repository.GetByInstallationIdAsync(device.InstallationId, default))
            .ReturnsAsync(device);
        var service = CreateService();

        var action = () => service.RegisterAsync(
            new RegisterDeviceRequest
            {
                InstallationId = device.InstallationId,
                Platform = "Android"
            },
            currentToken,
            Guid.NewGuid(),
            default);

        await action.Should().ThrowAsync<DeviceRegistrationException>()
            .Where(exception => exception.Code == DeviceProblemCodes.OwnerConflict);
        _repository.Verify(repository =>
            repository.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait("ScenarioId", "STEP24-DEVICE-INACTIVE-007")]
    public async Task RegisterAsync_InactiveOwnedDevice_CannotBeRecovered()
    {
        var userId = Guid.NewGuid();
        var device = CreateDevice(DeviceTokenHasher.Hash(Token(17)));
        device.UserId = userId;
        device.IsActive = false;
        _repository.Setup(repository =>
                repository.GetByInstallationIdAsync(device.InstallationId, default))
            .ReturnsAsync(device);
        var service = CreateService();

        var action = () => service.RegisterAsync(
            new RegisterDeviceRequest
            {
                InstallationId = device.InstallationId,
                Platform = "Android"
            },
            null,
            userId,
            default);

        await action.Should().ThrowAsync<DeviceRegistrationException>()
            .Where(exception => exception.Code == DeviceProblemCodes.TokenInactive);
    }

    [Fact]
    [Trait("ScenarioId", "STEP24-DEVICE-ROTATE-BIND-008")]
    public async Task RegisterAsync_CurrentTokenAndCustomer_BindsUnownedInstallation()
    {
        var currentToken = Token(18);
        var device = CreateDevice(DeviceTokenHasher.Hash(currentToken));
        var userId = Guid.NewGuid();
        _repository.Setup(repository =>
                repository.GetByInstallationIdAsync(device.InstallationId, default))
            .ReturnsAsync(device);
        _tokenGenerator.Setup(generator => generator.Generate()).Returns(Token(19));
        var service = CreateService();

        await service.RegisterAsync(
            new RegisterDeviceRequest
            {
                InstallationId = device.InstallationId,
                Platform = "Android"
            },
            currentToken,
            userId,
            default);

        device.UserId.Should().Be(userId);
    }

    private DeviceRegistrationService CreateService() =>
        new(
            _repository.Object,
            _tokenGenerator.Object,
            new TestTimeProvider(Now),
            Options.Create(new DeviceTokenOptions { LifetimeDays = 90 }),
            _logger.Object);

    private static CustomerDevice CreateDevice(byte[]? tokenHash = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            InstallationId = Guid.NewGuid(),
            Platform = "iOS",
            TokenHash = tokenHash ?? new byte[32],
            CreatedAt = Now,
            UpdatedAt = Now,
            ExpiresAt = Now.AddDays(1)
        };

    private static bool Capture(CustomerDevice device, out CustomerDevice captured)
    {
        captured = device;
        return true;
    }

    private static string Token(byte value) =>
        WebEncoders.Base64UrlEncode(Enumerable.Repeat(value, 32).ToArray());

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
