namespace GhseeliApis.Models;

public sealed class CustomerDevice
{
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
