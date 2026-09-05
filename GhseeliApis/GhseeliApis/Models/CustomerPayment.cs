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
    public string Provider { get; set; } = "Lahza";
    public string? ProviderReference { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? CheckoutUrl { get; set; }
    public string InitializationState { get; set; } = PaymentInitializationStates.NotStarted;
    public Guid? InitializationLeaseOwnerToken { get; set; }
    public DateTimeOffset? InitializationLeaseExpiresAtUtc { get; set; }
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

public sealed class PaymentWebhookEventRecord
{
    public string Provider { get; set; } = "Lahza";
    public string EventId { get; set; } = string.Empty;
    public string BodyHash { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string State { get; set; } = "Processing";
    public string? DispositionReason { get; set; }
    public Guid? CustomerPaymentId { get; set; }
    public CustomerPayment? CustomerPayment { get; set; }
    public string? ProviderReference { get; set; }
    public string? ProviderTransactionId { get; set; }
    public long? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public static class PaymentInitializationStates
{
    public const string NotStarted = "NotStarted";
    public const string Initialized = "Initialized";
    public const string Ambiguous = "Ambiguous";
    public const string Legacy = "Legacy";
}
