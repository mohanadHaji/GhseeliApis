namespace GhseeliApis.Models;

public sealed class CatalogCategoryReadModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceCategoryId { get; set; }
    public Guid ProviderId { get; set; }
    public CatalogProviderReadModel Provider { get; set; } = null!;
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public int DisplayOrder { get; set; }

    public ICollection<CatalogOfferingReadModel> Offerings { get; set; } =
        new List<CatalogOfferingReadModel>();
}
