using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.Vehicles;

namespace GhseeliApis.DTOs.Catalog;

public sealed class GetCatalogCategoriesRequest
{
    public string? Language { get; set; }
    public Guid? BusinessId { get; set; }
    public bool Refresh { get; set; }
}

public sealed class GetCatalogBusinessesRequest
{
    public string? Language { get; set; }
    public Guid? BusinessVerticalId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Search { get; set; }
    public int? Top { get; set; }
    public bool Refresh { get; set; }
}

public sealed class GetCatalogBusinessOfferingsRequest
{
    public string? Language { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? CategoryId { get; set; }
    public bool Refresh { get; set; }
}

public sealed class GetCatalogResourceRequest
{
    public string? Language { get; set; }
    public bool Refresh { get; set; }
}

public sealed class GetAvailableSlotsRequest
{
    public DateOnly Date { get; set; }
    public long? ExpectedCatalogVersion { get; set; }
    public CatalogAvailableSlotsLocationRequest? CustomerLocation { get; set; }
    public IReadOnlyCollection<CatalogAvailableSlotsItemRequest> Items { get; set; } =
        Array.Empty<CatalogAvailableSlotsItemRequest>();
    public bool IncludeUnavailable { get; set; }
    public string? Language { get; set; }
}

public sealed class AvailabilitySearchRequest
{
    public VehicleType VehicleType { get; set; }
    public DateOnly Date { get; set; }
    [JsonConverter(typeof(HourMinuteSecondTimeOnlyJsonConverter))]
    public TimeOnly PreferredLocalTime { get; set; }
    public Guid? BusinessVerticalId { get; set; }
    public Guid? CategoryId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Language { get; set; }
}

public sealed class GetBusinessVerticalsRequest
{
    public string? Language { get; set; }
}

public sealed class CatalogBusinessVerticalsResponse
{
    public string Language { get; set; } = string.Empty;
    public IReadOnlyCollection<CatalogBusinessVerticalResponse> BusinessVerticals { get; set; } =
        Array.Empty<CatalogBusinessVerticalResponse>();
}

public sealed class CatalogBusinessVerticalResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? ColorHex { get; set; }
    public string? BadgeCode { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class AvailabilitySearchResponse
{
    public string Language { get; set; } = string.Empty;
    public bool IsAdvisory { get; set; } = true;
    public VehicleType VehicleType { get; set; }
    public DateOnly Date { get; set; }
    [JsonConverter(typeof(HourMinuteSecondTimeOnlyJsonConverter))]
    public TimeOnly PreferredLocalTime { get; set; }
    public IReadOnlyCollection<AvailabilitySearchResult> Results { get; set; } =
        Array.Empty<AvailabilitySearchResult>();
}

public sealed class HourMinuteSecondTimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
    private const string Format = "HH:mm:ss";

    public override TimeOnly Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !TimeOnly.TryParseExact(
                reader.GetString(),
                Format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var value))
        {
            throw new JsonException($"The time must use the {Format} format.");
        }

        return value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        TimeOnly value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}

public sealed class AvailabilitySearchResult
{
    public Guid BusinessId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string BranchAddress { get; set; } = string.Empty;
    public bool IsFavourite { get; set; }
    public decimal AverageRating { get; set; }
    public int RatingCount { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public DateTime SlotStartUtc { get; set; }
    public DateTime SlotStartLocal { get; set; }
    public int ConfiguredCapacity { get; set; }
    public int RemainingCapacity { get; set; }
}

public sealed class CatalogAvailableSlotsLocationRequest
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public sealed class CatalogAvailableSlotsItemRequest
{
    public Guid OfferingId { get; set; }
    public IReadOnlyCollection<CatalogAvailableSlotsSelectionRequest> SelectedAddons { get; set; } =
        Array.Empty<CatalogAvailableSlotsSelectionRequest>();
}

public sealed class CatalogAvailableSlotsSelectionRequest
{
    public Guid AddonChoiceId { get; set; }
    public int Quantity { get; set; }
}

public sealed class CatalogAvailableSlotsResponse
{
    public Guid BusinessId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly Date { get; set; }
    public string? TimeZoneId { get; set; }
    public long CatalogVersion { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int TotalDurationMinutes { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public IReadOnlyList<CatalogAvailableSlotResponse> Slots { get; set; } =
        Array.Empty<CatalogAvailableSlotResponse>();
}

public sealed class CatalogAvailableSlotResponse
{
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public DateTime StartLocal { get; set; }
    public DateTime EndLocal { get; set; }
    public int ConfiguredCapacity { get; set; }
    public int RemainingCapacity { get; set; }
    public bool IsAvailable { get; set; }
}

public sealed class CatalogCategoriesResponse
{
    public string Language { get; set; } = string.Empty;
    public IReadOnlyCollection<CatalogCategoryResponse> Categories { get; set; } =
        Array.Empty<CatalogCategoryResponse>();
}

public sealed class CatalogBusinessesResponse
{
    public string Language { get; set; } = string.Empty;
    public IReadOnlyCollection<CatalogBusinessResponse> Businesses { get; set; } =
        Array.Empty<CatalogBusinessResponse>();
}

public sealed class CatalogBusinessDetailResponse
{
    public string Language { get; set; } = string.Empty;
    public CatalogBusinessResponse Business { get; set; } = new();
}

public sealed class CatalogBusinessOfferingsResponse
{
    public string Language { get; set; } = string.Empty;
    public CatalogBusinessResponse Business { get; set; } = new();
    public IReadOnlyCollection<CatalogOfferingResponse> Offerings { get; set; } =
        Array.Empty<CatalogOfferingResponse>();
}

public sealed class CatalogOfferingDetailResponse
{
    public string Language { get; set; } = string.Empty;
    public CatalogOfferingResponse Offering { get; set; } = new();
}

public sealed class CatalogBusinessResponse
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Phone { get; set; }
    public bool IsFavourite { get; set; }
    public decimal AverageRating { get; set; }
    public int RatingCount { get; set; }
    public CatalogBusinessVerticalResponse BusinessVertical { get; set; } = new();
    public CatalogMetadataResponse Catalog { get; set; } = new();
    public IReadOnlyCollection<CatalogBranchResponse> Branches { get; set; } =
        Array.Empty<CatalogBranchResponse>();
}

public sealed class CatalogBusinessContextResponse
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsFavourite { get; set; }
    public decimal AverageRating { get; set; }
    public int RatingCount { get; set; }
    public CatalogBusinessVerticalResponse BusinessVertical { get; set; } = new();
    public CatalogMetadataResponse Catalog { get; set; } = new();
}

public sealed class CatalogMetadataResponse
{
    public long Version { get; set; }
    public DateTimeOffset? GeneratedAtUtc { get; set; }
    public DateTimeOffset? RefreshedAtUtc { get; set; }
    public DateTimeOffset? FreshUntilUtc { get; set; }
    public DateTimeOffset? MaxStaleUntilUtc { get; set; }
    public bool IsStale { get; set; }
}

public sealed class CatalogBranchResponse
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public CatalogServiceAreaResponse ServiceArea { get; set; } = new();
}

public sealed class CatalogBranchContextResponse
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class CatalogServiceAreaResponse
{
    public bool UsesBranchCoordinates { get; set; }
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double RadiusKm { get; set; }
}

public sealed class CatalogCategoryResponse
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? ColorHex { get; set; }
    public int DisplayOrder { get; set; }
    public CatalogBusinessContextResponse Business { get; set; } = new();
}

public sealed class CatalogCategoryContextResponse
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class CatalogOfferingResponse
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Qualifier { get; set; }
    public CatalogOfferingBadgeCode? BadgeCode { get; set; }
    public decimal BasePrice { get; set; }
    public int DurationMinutes { get; set; }
    public string? ImageUrl { get; set; }
    public string? ReferenceCode { get; set; }
    public int DisplayOrder { get; set; }
    public CatalogBusinessContextResponse Business { get; set; } = new();
    public CatalogCategoryContextResponse Category { get; set; } = new();
    public CatalogBranchContextResponse? Branch { get; set; }
    public IReadOnlyCollection<CatalogAddonGroupResponse> AddonGroups { get; set; } =
        Array.Empty<CatalogAddonGroupResponse>();
}

public sealed class CatalogAddonGroupResponse
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SelectionType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public int MinimumSelections { get; set; }
    public int? MaximumSelections { get; set; }
    public int DisplayOrder { get; set; }
    public IReadOnlyCollection<CatalogAddonChoiceResponse> Choices { get; set; } =
        Array.Empty<CatalogAddonChoiceResponse>();
}

public sealed class CatalogAddonChoiceResponse
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal PriceAdjustment { get; set; }
    public int DurationAdjustmentMinutes { get; set; }
    public int DefaultQuantity { get; set; }
    public int DisplayOrder { get; set; }
}
