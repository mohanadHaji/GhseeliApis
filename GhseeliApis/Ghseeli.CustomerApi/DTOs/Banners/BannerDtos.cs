namespace GhseeliApis.DTOs.Banners;

public sealed class CreateBannerRequest
{
    public string ImageUrl { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; }
}

public sealed class UpdateBannerRequest
{
    public string ImageUrl { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; }
    public string ExpectedRowVersion { get; init; } = string.Empty;
}

public sealed class PublicBannerResponse
{
    public Guid Id { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
}

public sealed class PublicBannersResponse
{
    public IReadOnlyList<PublicBannerResponse> Banners { get; init; } = [];
}

public sealed class AdminBannerResponse
{
    public Guid Id { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class AdminBannersResponse
{
    public IReadOnlyList<AdminBannerResponse> Banners { get; init; } = [];
}
