using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Devices;
using GhseeliApis.Models;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GhseeliApis.Services.Devices;

public interface IDeviceRegistrationService
{
    Task<RegisterDeviceResponse> RegisterAsync(
        RegisterDeviceRequest request,
        string? currentToken,
        CancellationToken cancellationToken);

    Task<DeviceAuthenticationResult> AuthenticateAsync(
        string token,
        CancellationToken cancellationToken);

    Task UpdateLastSeenAsync(Guid deviceId, CancellationToken cancellationToken);
}

public sealed class DeviceAuthenticationResult
{
    private DeviceAuthenticationResult(
        bool isAuthenticated,
        Guid? deviceId,
        Guid? installationId,
        string? code)
    {
        IsAuthenticated = isAuthenticated;
        DeviceId = deviceId;
        InstallationId = installationId;
        Code = code;
    }

    public bool IsAuthenticated { get; }
    public Guid? DeviceId { get; }
    public Guid? InstallationId { get; }
    public string? Code { get; }

    public static DeviceAuthenticationResult Success(Guid deviceId, Guid installationId) =>
        new(true, deviceId, installationId, null);

    public static DeviceAuthenticationResult Failure(string code) =>
        new(false, null, null, code);
}

public sealed class DeviceRegistrationException : Exception
{
    public DeviceRegistrationException(string code, int statusCode, string message)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public sealed class DeviceRegistrationService : IDeviceRegistrationService
{
    private readonly IDeviceRepository _repository;
    private readonly IDeviceTokenGenerator _tokenGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly DeviceTokenOptions _options;
    private readonly IAppLogger _logger;

    public DeviceRegistrationService(
        IDeviceRepository repository,
        IDeviceTokenGenerator tokenGenerator,
        TimeProvider timeProvider,
        IOptions<DeviceTokenOptions> options,
        IAppLogger logger)
    {
        _repository = repository;
        _tokenGenerator = tokenGenerator;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RegisterDeviceResponse> RegisterAsync(
        RegisterDeviceRequest request,
        string? currentToken,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var existing = await _repository.GetByInstallationIdAsync(
            request.InstallationId,
            cancellationToken);
        var token = _tokenGenerator.Generate();

        if (existing is null)
        {
            var device = new CustomerDevice
            {
                Id = Guid.NewGuid(),
                InstallationId = request.InstallationId,
                Platform = NormalizePlatform(request.Platform),
                AppVersion = request.AppVersion,
                TokenHash = DeviceTokenHasher.Hash(token),
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = now.AddDays(_options.LifetimeDays)
            };

            await _repository.AddAsync(device, cancellationToken);
            try
            {
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                _logger.LogWarning(
                    $"Device registration conflict for installation {request.InstallationId}: {exception.GetType().Name}");
                throw new DeviceRegistrationException(
                    DeviceProblemCodes.ConcurrencyConflict,
                    StatusCodes.Status409Conflict,
                    "The device installation was registered concurrently.");
            }

            _logger.LogInfo($"Registered device installation {request.InstallationId}.");
            return Map(device, token);
        }

        if (string.IsNullOrWhiteSpace(currentToken))
        {
            throw new DeviceRegistrationException(
                DeviceProblemCodes.RegistrationConflict,
                StatusCodes.Status409Conflict,
                "This installation is already registered. Supply its current device token to rotate it.");
        }

        if (existing.ExpiresAt <= now)
        {
            throw new DeviceRegistrationException(
                DeviceProblemCodes.TokenExpired,
                StatusCodes.Status401Unauthorized,
                "The current device token has expired.");
        }

        if (!DeviceTokenHasher.Matches(existing.TokenHash, currentToken))
        {
            throw new DeviceRegistrationException(
                DeviceProblemCodes.RotationUnauthorized,
                StatusCodes.Status401Unauthorized,
                "The current device token is invalid for this installation.");
        }

        existing.Platform = NormalizePlatform(request.Platform);
        existing.AppVersion = request.AppVersion;
        existing.TokenHash = DeviceTokenHasher.Hash(token);
        existing.UpdatedAt = now;
        existing.ExpiresAt = now.AddDays(_options.LifetimeDays);

        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DeviceRegistrationException(
                DeviceProblemCodes.ConcurrencyConflict,
                StatusCodes.Status409Conflict,
                "The device token was rotated concurrently.");
        }

        _logger.LogInfo($"Rotated device token for installation {request.InstallationId}.");
        return Map(existing, token);
    }

    public async Task<DeviceAuthenticationResult> AuthenticateAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (!DeviceTokenHasher.IsValidFormat(token))
        {
            return DeviceAuthenticationResult.Failure(DeviceProblemCodes.TokenInvalid);
        }

        var device = await _repository.GetByTokenHashAsync(
            DeviceTokenHasher.Hash(token),
            cancellationToken);
        if (device is null || !DeviceTokenHasher.Matches(device.TokenHash, token))
        {
            return DeviceAuthenticationResult.Failure(DeviceProblemCodes.TokenInvalid);
        }

        if (device.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            return DeviceAuthenticationResult.Failure(DeviceProblemCodes.TokenExpired);
        }

        return DeviceAuthenticationResult.Success(device.Id, device.InstallationId);
    }

    public async Task UpdateLastSeenAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        // Last-seen is operational metadata; authentication correctness never depends on this write.
        var device = await _repository.GetByIdAsync(
            deviceId,
            cancellationToken);
        if (device is null)
        {
            return;
        }

        device.LastSeenAt = _timeProvider.GetUtcNow();
        await _repository.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizePlatform(string platform) =>
        platform.Equals("ios", StringComparison.OrdinalIgnoreCase) ? "iOS" : "Android";

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlException &&
                sqlException.Number is 2601 or 2627)
            {
                return true;
            }
        }

        return false;
    }

    private static RegisterDeviceResponse Map(CustomerDevice device, string token) =>
        new()
        {
            DeviceId = device.Id,
            InstallationId = device.InstallationId,
            Platform = device.Platform,
            AppVersion = device.AppVersion,
            Token = token,
            ExpiresAt = device.ExpiresAt
        };
}
