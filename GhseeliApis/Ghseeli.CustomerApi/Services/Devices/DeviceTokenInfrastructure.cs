using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace GhseeliApis.Services.Devices;

public static class DeviceTokenDefaults
{
    public const string HeaderName = "X-Device-Token";
    public const int TokenByteLength = 32;
    public const int EncodedTokenLength = 43;
}

public static class DeviceProblemCodes
{
    public const string TokenMissing = "device_token_missing";
    public const string TokenInvalid = "device_token_invalid";
    public const string TokenExpired = "device_token_expired";
    public const string TokenInactive = "device_token_inactive";
    public const string RegistrationConflict = "device_registration_conflict";
    public const string RotationUnauthorized = "device_rotation_unauthorized";
    public const string ConcurrencyConflict = "device_registration_concurrency_conflict";
}

public sealed class DeviceTokenOptions
{
    public const string SectionName = "DeviceTokens";
    public int LifetimeDays { get; set; } = 90;
}

public sealed class DeviceTokenOptionsValidator : IValidateOptions<DeviceTokenOptions>
{
    public ValidateOptionsResult Validate(string? name, DeviceTokenOptions options) =>
        options.LifetimeDays is >= 1 and <= 3660
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{DeviceTokenOptions.SectionName}:LifetimeDays must be between 1 and 3660.");
}

public interface IDeviceTokenGenerator
{
    string Generate();
}

public sealed class DeviceTokenGenerator : IDeviceTokenGenerator
{
    public string Generate() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(DeviceTokenDefaults.TokenByteLength));
}

public static class DeviceTokenHasher
{
    public static byte[] Hash(string token) =>
        SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token));

    public static bool IsValidFormat(string? token)
    {
        if (token is null || token.Length != DeviceTokenDefaults.EncodedTokenLength)
        {
            return false;
        }

        try
        {
            return WebEncoders.Base64UrlDecode(token).Length == DeviceTokenDefaults.TokenByteLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool Matches(byte[] storedHash, string token)
    {
        if (storedHash.Length != SHA256.HashSizeInBytes || !IsValidFormat(token))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(storedHash, Hash(token));
    }
}
