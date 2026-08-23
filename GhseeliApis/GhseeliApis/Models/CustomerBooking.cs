namespace GhseeliApis.Models;

public sealed class CustomerBooking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublicReference { get; set; } = Guid.NewGuid();
    public Guid OrderGuid { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid OwnerDeviceId { get; set; }
    public Guid BusinessReservationId { get; set; }
    public Guid BusinessWorkOrderId { get; set; }
    public Guid BusinessSourceId { get; set; }
    public Guid BranchSourceId { get; set; }
    public long CatalogVersion { get; set; }
    public int ConfirmedDraftVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public long BusinessStatusSequence { get; set; }
    public DateTimeOffset StatusChangedAtUtc { get; set; }
    public DateTimeOffset RequestedSlotStartUtc { get; set; }
    public DateTimeOffset RequestedSlotEndUtc { get; set; }
    public string ProviderNameAr { get; set; } = string.Empty;
    public string? ProviderNameHe { get; set; }
    public string BranchNameAr { get; set; } = string.Empty;
    public string? BranchNameHe { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    public string? LicensePlate { get; set; }
    public string? VehicleMake { get; set; }
    public string? VehicleModel { get; set; }
    public string? VehicleColor { get; set; }
    public string AddressLine { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Area { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal BaseSubtotal { get; set; }
    public decimal AddonSubtotal { get; set; }
    public decimal ItemSubtotal { get; set; }
    public decimal ServiceFee { get; set; }
    public string ServiceFeeMode { get; set; } = string.Empty;
    public decimal ServiceFeeFlatAmount { get; set; }
    public decimal ServiceFeePercentageRate { get; set; }
    public decimal TaxableSubtotal { get; set; }
    public decimal TaxRatePercent { get; set; }
    public bool TaxAppliesToServiceFee { get; set; }
    public decimal Tax { get; set; }
    public decimal GrandTotal { get; set; }
    public int TotalDurationMinutes { get; set; }
    public DateTimeOffset QuotedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public ICollection<CustomerBookingItem> Items { get; set; } = new List<CustomerBookingItem>();
    public ICollection<ProcessedBookingStatusMessage> ProcessedStatusMessages { get; set; } =
        new List<ProcessedBookingStatusMessage>();
}

public sealed class BookingConfirmationAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderGuid { get; set; }
    public Guid BookingReference { get; set; }
    public Guid UserId { get; set; }
    public Guid OwnerDeviceId { get; set; }
    public int DraftVersion { get; set; }
    public string ReservationRequestJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class CustomerBookingItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerBookingId { get; set; }
    public CustomerBooking CustomerBooking { get; set; } = null!;
    public Guid OfferingSourceId { get; set; }
    public string ServiceNameAr { get; set; } = string.Empty;
    public string? ServiceNameHe { get; set; }
    public int DisplayOrder { get; set; }
    public decimal BaseSubtotal { get; set; }
    public decimal AddonSubtotal { get; set; }
    public decimal ItemSubtotal { get; set; }
    public int TotalDurationMinutes { get; set; }
    public ICollection<CustomerBookingSelection> Selections { get; set; } =
        new List<CustomerBookingSelection>();
}

public sealed class CustomerBookingSelection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerBookingItemId { get; set; }
    public CustomerBookingItem CustomerBookingItem { get; set; } = null!;
    public Guid AddonGroupSourceId { get; set; }
    public Guid AddonChoiceSourceId { get; set; }
    public string AddonGroupNameAr { get; set; } = string.Empty;
    public string? AddonGroupNameHe { get; set; }
    public string AddonChoiceNameAr { get; set; } = string.Empty;
    public string? AddonChoiceNameHe { get; set; }
    public string SelectionType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceAdjustment { get; set; }
    public decimal TotalPriceAdjustment { get; set; }
    public int UnitDurationAdjustmentMinutes { get; set; }
    public int TotalDurationAdjustmentMinutes { get; set; }
    public bool IsDefaultApplied { get; set; }
    public int DisplayOrder { get; set; }
}
