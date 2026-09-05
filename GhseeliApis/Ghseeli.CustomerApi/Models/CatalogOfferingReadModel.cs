namespace GhseeliApis.Models;

public sealed class CatalogOfferingReadModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceOfferingId { get; set; }
    public Guid CategoryId { get; set; }
    public CatalogCategoryReadModel Category { get; set; } = null!;
    public Guid? BranchId { get; set; }
    public CatalogBranchReadModel? Branch { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public decimal BasePrice { get; set; }
    public int DurationMinutes { get; set; }
    public string? ImageUrl { get; set; }
    public string? ReferenceCode { get; set; }
    public int DisplayOrder { get; set; }

    public ICollection<CatalogAddonGroupReadModel> AddonGroups { get; set; } =
        new List<CatalogAddonGroupReadModel>();
}
