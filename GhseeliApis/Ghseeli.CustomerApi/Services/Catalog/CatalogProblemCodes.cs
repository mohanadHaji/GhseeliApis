namespace GhseeliApis.Services.Catalog;

public static class CatalogProblemCodes
{
    public const string AvailabilityUnavailable = "availability_unavailable";
    public const string Unavailable = "catalog_unavailable";
    public const string BusinessNotFound = "catalog_business_not_found";
    public const string OfferingNotFound = "catalog_offering_not_found";
    public const string StaleVersion = "catalog_stale_version";
    public const string FilterMismatch = "catalog_filter_mismatch";
    public const string TopInvalid = "catalog_top_invalid";
}
