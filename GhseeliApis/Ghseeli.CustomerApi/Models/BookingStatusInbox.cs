namespace GhseeliApis.Models;

public sealed class CustomerInternalServiceNonce
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ServiceId { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public DateTimeOffset AcceptedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

public sealed class CustomerInternalIdempotencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ServiceId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public CustomerInternalIdempotencyState State { get; set; }
    public Guid? OwnerToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseContentType { get; set; }
    public byte[]? ResponseBody { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

public enum CustomerInternalIdempotencyState
{
    InProgress = 0,
    Completed = 1
}

public sealed class ProcessedBookingStatusMessage
{
    public Guid EventId { get; set; }
    public Guid CustomerBookingId { get; set; }
    public CustomerBooking CustomerBooking { get; set; } = null!;
    public string RequestHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public bool Applied { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
