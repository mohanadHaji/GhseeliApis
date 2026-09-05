namespace GhseeliApis.Models;

public sealed class CheckoutDraftPricingItemSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PricingSnapshotId { get; set; }
    public CheckoutDraftPricingSnapshot PricingSnapshot { get; set; } = null!;
    public Guid OfferingSourceId { get; set; }
    public int DisplayOrder { get; set; }
    public decimal BaseSubtotal { get; set; }
    public decimal AddonSubtotal { get; set; }
    public decimal ItemSubtotal { get; set; }
    public int TotalDurationMinutes { get; set; }

    public ICollection<CheckoutDraftPricingSelectionSnapshot> Selections { get; set; } =
        new List<CheckoutDraftPricingSelectionSnapshot>();
}
