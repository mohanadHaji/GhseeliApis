namespace GhseeliApis.Models;

public sealed class CatalogAddonChoiceReadModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceAddonChoiceId { get; set; }
    public Guid AddonGroupId { get; set; }
    public CatalogAddonGroupReadModel AddonGroup { get; set; } = null!;
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public decimal PriceAdjustment { get; set; }
    public int DurationAdjustmentMinutes { get; set; }
    public int DefaultQuantity { get; set; }
    public int DisplayOrder { get; set; }
}
