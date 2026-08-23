namespace Ghseeli.BusinessApi.Models;

public static class BookingStatusOutboxStates
{
    public const string Pending = "Pending";
    public const string Leased = "Leased";
    public const string Delivered = "Delivered";
    public const string DeadLetter = "DeadLetter";
}

public sealed class BookingStatusOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppointmentReservationId { get; set; }
    public AppointmentReservation AppointmentReservation { get; set; } = null!;
    public string RequestJson { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public Guid WorkOrderPublicId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string DeliveryState { get; set; } = BookingStatusOutboxStates.Pending;
    public int DeliveryGeneration { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public DateTimeOffset? DeadLetteredAtUtc { get; set; }
    public Guid? LeaseToken { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset? RequeuedAtUtc { get; set; }
    public Guid? RequeuedByAdminUserId { get; set; }
    public string? RequeueRequestId { get; set; }
    public ICollection<BookingStatusRequeueHistory> RequeueHistory { get; set; } = [];
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class BookingStatusRequeueHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingStatusOutboxMessageId { get; set; }
    public BookingStatusOutboxMessage BookingStatusOutboxMessage { get; set; } = null!;
    public int Generation { get; set; }
    public Guid AdminUserId { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public DateTimeOffset RequeuedAtUtc { get; set; }
}
