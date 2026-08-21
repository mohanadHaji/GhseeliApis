using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Devices;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using GhseeliApis.Services.Devices;
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
            new RegisterDeviceRequest { InstallationId = installationId, Platform = "ios", AppVersion = "1.2.3" },
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
            new RegisterDeviceRequest { InstallationId = device.InstallationId, Platform = "android" },
            oldToken,
            default);

        response.Token.Should().Be(rotatedToken);
        device.TokenHash.Should().NotEqual(oldHash);
        device.Platform.Should().Be("Android");
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
