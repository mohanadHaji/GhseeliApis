namespace Ghseeli.IntegrationContracts.Bookings;

public static class BookingStatusContract
{
    public const string Version = "v1";
}

public static class BookingStatuses
{
    public const string LegacyReserved = "Reserved";
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Pending, Confirmed, InProgress, Completed, Cancelled, NoShow],
        StringComparer.Ordinal);

    public static readonly IReadOnlyList<string> CapacityOccupying =
        [Pending, LegacyReserved, Confirmed, InProgress];

    public static readonly IReadOnlySet<string> Terminal = new HashSet<string>(
        [Completed, Cancelled, NoShow],
        StringComparer.Ordinal);

    public static string Normalize(string status) =>
        string.Equals(status, LegacyReserved, StringComparison.Ordinal)
            ? Pending
            : status;

    public static bool IsCapacityOccupying(string status) =>
        CapacityOccupying.Contains(status, StringComparer.Ordinal);

    public static bool IsTerminal(string status) =>
        Terminal.Contains(Normalize(status));

    public static bool CanTransition(string current, string target) =>
        Normalize(current) switch
        {
            Pending => target is Confirmed or Cancelled,
            Confirmed => target is InProgress or Cancelled or NoShow,
            InProgress => target is Completed or Cancelled or NoShow,
            _ => false
        };
}

public static class BookingStatusErrorCodes
{
    public const string Invalid = "BOOKING_STATUS_INVALID";
    public const string UnsupportedMediaType = "BOOKING_STATUS_UNSUPPORTED_MEDIA_TYPE";
    public const string RequestBodyTooLarge = "BOOKING_STATUS_REQUEST_BODY_TOO_LARGE";
    public const string NotFound = "BOOKING_NOT_FOUND";
    public const string ReferenceMismatch = "BOOKING_REFERENCE_MISMATCH";
    public const string TransitionInvalid = "BOOKING_TRANSITION_INVALID";
    public const string TransitionConflict = "BOOKING_TRANSITION_CONFLICT";
    public const string EventConflict = "BOOKING_STATUS_EVENT_CONFLICT";
}

public sealed class BookingStatusChangedMessage
{
    public string ContractVersion { get; set; } = BookingStatusContract.Version;
    public Guid EventId { get; set; }
    public Guid BookingReference { get; set; }
    public Guid ReservationId { get; set; }
    public Guid WorkOrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class BookingStatusCallbackResponse
{
    public string ContractVersion { get; set; } = BookingStatusContract.Version;
    public Guid EventId { get; set; }
    public Guid BookingReference { get; set; }
    public string Status { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public bool Applied { get; set; }
    public bool Stale { get; set; }
}

public sealed class BookingStatusReadResponse
{
    public string ContractVersion { get; set; } = BookingStatusContract.Version;
    public Guid BookingReference { get; set; }
    public Guid ReservationId { get; set; }
    public Guid WorkOrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
}

public class AuthoritativeBookingStatusResponse
{
    public string ContractVersion { get; set; } = BookingStatusContract.Version;
    public Guid BookingReference { get; set; }
    public Guid ReservationId { get; set; }
    public Guid WorkOrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
}

public sealed class TransitionWorkOrderRequest
{
    public string Status { get; set; } = string.Empty;
}

public sealed class TransitionWorkOrderResponse : AuthoritativeBookingStatusResponse
{
    public Guid EventId { get; set; }
}
