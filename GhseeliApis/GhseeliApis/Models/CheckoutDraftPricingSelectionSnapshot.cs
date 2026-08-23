namespace GhseeliApis.Models;

public sealed class CheckoutDraftPricingSelectionSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PricingItemSnapshotId { get; set; }
    public CheckoutDraftPricingItemSnapshot PricingItemSnapshot { get; set; } = null!;
    public Guid AddonGroupSourceId { get; set; }
    public Guid AddonChoiceSourceId { get; set; }
    public string SelectionType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceAdjustment { get; set; }
    public decimal TotalPriceAdjustment { get; set; }
    public int UnitDurationAdjustmentMinutes { get; set; }
    public int TotalDurationAdjustmentMinutes { get; set; }
    public bool IsDefaultApplied { get; set; }
    public int DisplayOrder { get; set; }
}
