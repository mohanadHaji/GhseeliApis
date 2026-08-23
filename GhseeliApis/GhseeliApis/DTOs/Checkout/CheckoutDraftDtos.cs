using System.Text.Json.Serialization;

namespace GhseeliApis.DTOs.Checkout;

public sealed class GetCheckoutDraftRequest
{
    public string? Language { get; set; }
}

public abstract class CheckoutDraftMutationRequestBase
{
    public Guid BusinessSourceId { get; set; }
    public Guid BranchSourceId { get; set; }
    public DateTimeOffset RequestedSlotStartUtc { get; set; }
    public CheckoutDraftVehicleRequest Vehicle { get; set; } = new();
    public CheckoutDraftLocationRequest Location { get; set; } = new();
    public IReadOnlyCollection<CheckoutDraftItemRequest> Items { get; set; } =
        Array.Empty<CheckoutDraftItemRequest>();
}

public sealed class CreateCheckoutDraftRequest : CheckoutDraftMutationRequestBase
{
}

public sealed class UpdateCheckoutDraftRequest : CheckoutDraftMutationRequestBase
{
    public int ExpectedVersion { get; set; }
}

public sealed class RepriceCheckoutDraftRequest
{
    public int ExpectedVersion { get; set; }
}

public sealed class CheckoutDraftVehicleRequest
{
    public string? VehicleType { get; set; }
    public string? LicensePlate { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Color { get; set; }
}

public sealed class CheckoutDraftLocationRequest
{
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public sealed class CheckoutDraftItemRequest
{
    public Guid OfferingSourceId { get; set; }
    public IReadOnlyCollection<CheckoutDraftSelectionRequest> Selections { get; set; } =
        Array.Empty<CheckoutDraftSelectionRequest>();
}

public sealed class CheckoutDraftSelectionRequest
{
    public Guid AddonChoiceSourceId { get; set; }
    public int Quantity { get; set; }
}

public sealed class CheckoutDraftResponse
{
    public string Language { get; set; } = string.Empty;
    public Guid OrderGuid { get; set; }
    public int Version { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool RequiresReprice { get; set; }
    public CheckoutDraftIntentResponse Intent { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CheckoutPricingSnapshotResponse? Pricing { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CheckoutPaymentCapabilitiesResponse? PaymentCapabilities { get; set; }
}

public sealed class DirectCheckoutPricingResponse
{
    public string Language { get; set; } = string.Empty;
    public CheckoutDraftIntentResponse Intent { get; set; } = new();
    public CheckoutPricingSnapshotResponse Pricing { get; set; } = new();
    public CheckoutPaymentCapabilitiesResponse PaymentCapabilities { get; set; } = new();
}

public sealed class CheckoutDraftIntentResponse
{
    public Guid BusinessSourceId { get; set; }
    public Guid BranchSourceId { get; set; }
    public long CatalogVersion { get; set; }
    public DateTimeOffset RequestedSlotStartUtc { get; set; }
    public CheckoutDraftVehicleResponse Vehicle { get; set; } = new();
    public CheckoutDraftLocationResponse Location { get; set; } = new();
    public IReadOnlyCollection<CheckoutDraftItemResponse> Items { get; set; } =
        Array.Empty<CheckoutDraftItemResponse>();
}

public sealed class CheckoutDraftVehicleResponse
{
    public string VehicleType { get; set; } = string.Empty;
    public string? LicensePlate { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Color { get; set; }
}

public sealed class CheckoutDraftLocationResponse
{
    public string AddressLine { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Area { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public sealed class CheckoutDraftItemResponse
{
    public Guid OfferingSourceId { get; set; }
    public IReadOnlyCollection<CheckoutDraftSelectionResponse> Selections { get; set; } =
        Array.Empty<CheckoutDraftSelectionResponse>();
}

public sealed class CheckoutDraftSelectionResponse
{
    public Guid AddonGroupSourceId { get; set; }
    public Guid AddonChoiceSourceId { get; set; }
    public int Quantity { get; set; }
}

public sealed class CheckoutPricingSnapshotResponse
{
    public long CatalogVersion { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset QuotedAtUtc { get; set; }
    public decimal BaseSubtotal { get; set; }
    public decimal AddonSubtotal { get; set; }
    public decimal ItemSubtotal { get; set; }
    public decimal ServiceFee { get; set; }
    public string ServiceFeeMode { get; set; } = string.Empty;
    public decimal ServiceFeeFlatAmount { get; set; }
    public decimal ServiceFeePercentageRate { get; set; }
    public decimal TaxableSubtotal { get; set; }
    public decimal TaxRatePercent { get; set; }
    public bool TaxAppliesToServiceFee { get; set; }
    public decimal Tax { get; set; }
    public decimal GrandTotal { get; set; }
    public int TotalDurationMinutes { get; set; }
    public IReadOnlyCollection<CheckoutPricingItemSnapshotResponse> Items { get; set; } =
        Array.Empty<CheckoutPricingItemSnapshotResponse>();
}

public sealed class CheckoutPricingItemSnapshotResponse
{
    public Guid OfferingSourceId { get; set; }
    public decimal BaseSubtotal { get; set; }
    public decimal AddonSubtotal { get; set; }
    public decimal ItemSubtotal { get; set; }
    public int TotalDurationMinutes { get; set; }
    public IReadOnlyCollection<CheckoutPricingSelectionSnapshotResponse> Selections { get; set; } =
        Array.Empty<CheckoutPricingSelectionSnapshotResponse>();
}

public sealed class CheckoutPricingSelectionSnapshotResponse
{
    public Guid AddonGroupSourceId { get; set; }
    public Guid AddonChoiceSourceId { get; set; }
    public string SelectionType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceAdjustment { get; set; }
    public decimal TotalPriceAdjustment { get; set; }
    public int UnitDurationAdjustmentMinutes { get; set; }
    public int TotalDurationAdjustmentMinutes { get; set; }
    public bool IsDefaultApplied { get; set; }
}

public sealed class CheckoutPaymentCapabilitiesResponse
{
    public IReadOnlyCollection<CheckoutPaymentMethodCapabilityResponse> Methods { get; set; } =
        Array.Empty<CheckoutPaymentMethodCapabilityResponse>();
}

public sealed class CheckoutPaymentMethodCapabilityResponse
{
    public string Method { get; set; } = string.Empty;
    public bool Enabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonCode { get; set; }
}
