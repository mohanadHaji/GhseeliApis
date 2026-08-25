namespace GhseeliApis.Models;

public sealed class CatalogProviderReadModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceCompanyId { get; set; }
    public string BusinessVerticalCode { get; set; } =
        BusinessVerticalSnapshotDefaults.CarWashCode;
    public bool IsEnabled { get; set; }
    public int DisplayOrder { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public string? Phone { get; set; }
    public long CatalogVersion { get; set; }
    public string? SnapshotHash { get; set; }
    public DateTimeOffset? SnapshotGeneratedAtUtc { get; set; }
    public DateTimeOffset? LastSuccessfulRefreshAtUtc { get; set; }
    public DateTimeOffset? LastAttemptedRefreshAtUtc { get; set; }
    public DateTimeOffset? LastFailedRefreshAtUtc { get; set; }
    public string? LastFailureCode { get; set; }
    public DateTimeOffset? RefreshLeaseAcquiredAtUtc { get; set; }
    public DateTimeOffset? RefreshLeaseExpiresAtUtc { get; set; }
    public string? RefreshLeaseToken { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<CatalogBranchReadModel> Branches { get; set; } =
        new List<CatalogBranchReadModel>();

    public ICollection<CatalogCategoryReadModel> Categories { get; set; } =
        new List<CatalogCategoryReadModel>();
}
