namespace Ghseeli.BusinessApi.Models;

public class AddonChoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AddonGroupId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public decimal PriceAdjustment { get; set; }
    public int DurationAdjustmentMinutes { get; set; }
    public int DefaultQuantity { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public AddonGroup AddonGroup { get; set; } = null!;
}
