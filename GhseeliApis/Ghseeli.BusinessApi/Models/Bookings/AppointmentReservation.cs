namespace Ghseeli.BusinessApi.Models;

public sealed class AppointmentReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid CustomerBookingReference { get; set; }
    public Guid OrderGuid { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public Guid BusinessVerticalId { get; set; } = BusinessVerticalDefaults.CarWashId;
    public string BusinessVerticalCode { get; set; } = BusinessVerticalDefaults.CarWashCode;
    public long CatalogVersion { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ItemSubtotal { get; set; }
    public int TotalDurationMinutes { get; set; }
    public DateTime RequestedSlotStartUtc { get; set; }
    public DateTime RequestedSlotEndUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public long StatusSequence { get; set; }
    public DateTimeOffset StatusChangedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public WorkOrder WorkOrder { get; set; } = null!;
    public BusinessVertical BusinessVertical { get; set; } = null!;
    public ICollection<BookingStatusOutboxMessage> StatusOutboxMessages { get; set; } =
        new List<BookingStatusOutboxMessage>();
}
