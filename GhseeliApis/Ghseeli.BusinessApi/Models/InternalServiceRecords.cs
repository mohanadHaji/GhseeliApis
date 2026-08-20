namespace Ghseeli.BusinessApi.Models;

public class InternalServiceNonce
{
    public Guid Id { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public class InternalServiceIdempotencyRecord
{
    public Guid Id { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public InternalServiceIdempotencyState State { get; set; }
    public int? StatusCode { get; set; }
    public string? ContentType { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public enum InternalServiceIdempotencyState
{
    InProgress = 0,
    Completed = 1
}
