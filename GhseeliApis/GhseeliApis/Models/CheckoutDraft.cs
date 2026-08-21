namespace GhseeliApis.Models;

public sealed class CheckoutDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderGuid { get; set; }
    public Guid OwnerDeviceId { get; set; }
    public Guid BusinessSourceId { get; set; }
    public Guid BranchSourceId { get; set; }
    public long CatalogVersion { get; set; }
    public int PublicVersion { get; set; }
    public DateTimeOffset RequestedSlotStartUtc { get; set; }
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
    public bool RequiresReprice { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<CheckoutDraftItem> Items { get; set; } = new List<CheckoutDraftItem>();
}
