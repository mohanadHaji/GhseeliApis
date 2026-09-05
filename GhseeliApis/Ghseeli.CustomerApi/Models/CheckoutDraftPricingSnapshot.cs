namespace GhseeliApis.Models;

public sealed class CheckoutDraftPricingSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CheckoutDraftId { get; set; }
    public CheckoutDraft CheckoutDraft { get; set; } = null!;
    public long CatalogVersion { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset QuotedAtUtc { get; set; }
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

    public ICollection<CheckoutDraftPricingItemSnapshot> Items { get; set; } =
        new List<CheckoutDraftPricingItemSnapshot>();
}
