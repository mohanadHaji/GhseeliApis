using GhseeliApis.Models.Enums;

namespace GhseeliApis.Models;

public sealed class CustomerPayment
{
    public Guid Id { get; set; }
    public Guid CustomerBookingId { get; set; }
    public CustomerBooking CustomerBooking { get; set; } = null!;
    public Guid UserId { get; set; }
    public Guid OwnerDeviceId { get; set; }
    public decimal Amount { get; set; }
    public long MinorAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string StripeIdempotencyKey { get; set; } = string.Empty;
    public string? PaymentIntentId { get; set; }
    public string? ChargeId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ClientSecret { get; set; }
    public string? ProviderPublishableKey { get; set; }
    public Guid? IntentLeaseOwnerToken { get; set; }
    public DateTimeOffset? IntentLeaseExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public ICollection<CustomerPaymentIdempotencyRecord> IdempotencyRecords { get; set; } =
        new List<CustomerPaymentIdempotencyRecord>();
}

public sealed class CustomerPaymentIdempotencyRecord
{
    public Guid Id { get; set; }
    public Guid CustomerPaymentId { get; set; }
    public CustomerPayment CustomerPayment { get; set; } = null!;
    public Guid UserId { get; set; }
    public Guid OwnerDeviceId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class StripeWebhookEventRecord
{
    public string EventId { get; set; } = string.Empty;
    public string BodyHash { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string State { get; set; } = "Processing";
    public string? DispositionReason { get; set; }
    public Guid? CustomerPaymentId { get; set; }
    public CustomerPayment? CustomerPayment { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? ChargeId { get; set; }
    public long? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
