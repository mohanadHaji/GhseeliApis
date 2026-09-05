namespace GhseeliApis.Models;

public sealed class CatalogBranchReadModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceBranchId { get; set; }
    public Guid ProviderId { get; set; }
    public CatalogProviderReadModel Provider { get; set; } = null!;
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string AddressAr { get; set; } = string.Empty;
    public string? AddressHe { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool HasPublishedServiceArea { get; set; }
    public bool UsesBranchCoordinates { get; set; }
    public double? ServiceAreaCenterLatitude { get; set; }
    public double? ServiceAreaCenterLongitude { get; set; }
    public double? ServiceAreaRadiusKm { get; set; }
    public string? AvailabilitySnapshotJson { get; set; }
    public int DisplayOrder { get; set; }

    public ICollection<CatalogOfferingReadModel> Offerings { get; set; } =
        new List<CatalogOfferingReadModel>();
}
