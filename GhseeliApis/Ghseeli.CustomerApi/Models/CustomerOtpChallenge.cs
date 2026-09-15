namespace GhseeliApis.Models;

public sealed class CustomerOtpChallenge
{
    public Guid Id { get; set; }
    public bool IsDemo { get; set; }
    public string NormalizedEmail { get; set; } = string.Empty;
    public byte[] CodeSalt { get; set; } = [];
    public byte[] CodeHash { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public int FailedAttemptCount { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
