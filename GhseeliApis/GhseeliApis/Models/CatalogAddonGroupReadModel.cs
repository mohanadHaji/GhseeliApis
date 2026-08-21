namespace GhseeliApis.Models;

public sealed class CatalogAddonGroupReadModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceAddonGroupId { get; set; }
    public Guid OfferingId { get; set; }
    public CatalogOfferingReadModel Offering { get; set; } = null!;
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public string SelectionType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public int MinimumSelections { get; set; }
    public int? MaximumSelections { get; set; }
    public int DisplayOrder { get; set; }

    public ICollection<CatalogAddonChoiceReadModel> Choices { get; set; } =
        new List<CatalogAddonChoiceReadModel>();
}
