namespace GhseeliApis.DTOs.Reviews;

public sealed class PutBusinessReviewRequest
{
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string? ExpectedRowVersion { get; set; }
}

public sealed class OwnedBusinessReviewResponse
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid BusinessId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class PublicBusinessReviewResponse
{
    public Guid Id { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string CustomerDisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class PublicBusinessReviewsResponse
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public decimal AverageRating { get; set; }
    public int RatingCount { get; set; }
    public IReadOnlyList<PublicBusinessReviewResponse> Items { get; set; } =
        Array.Empty<PublicBusinessReviewResponse>();
}

public sealed class GetBusinessReviewsRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Language { get; set; }
}

public sealed record BusinessRatingAggregate(
    Guid BusinessSourceId,
    decimal AverageRating,
    int RatingCount);
