namespace GhseeliApis.Services.Catalog;

public sealed class CatalogReadModelOptions
{
    public const string SectionName = "CatalogReadModel";

    public double FreshWindowSeconds { get; set; } = 300;
    public double MaxStaleWindowSeconds { get; set; } = 3600;
    public double LeaseDurationSeconds { get; set; } = 60;
    public List<CatalogProviderRegistrationOptions> Providers { get; set; } = [];
}

public sealed class CatalogProviderRegistrationOptions
{
    public Guid SourceCompanyId { get; set; }
    public bool Enabled { get; set; } = true;
    public int Order { get; set; }
}
