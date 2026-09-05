namespace GhseeliApis.DTOs.Devices;

public sealed class RegisterDeviceRequest
{
    public Guid InstallationId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
}

public sealed class RegisterDeviceResponse
{
    public Guid DeviceId { get; set; }
    public Guid InstallationId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
