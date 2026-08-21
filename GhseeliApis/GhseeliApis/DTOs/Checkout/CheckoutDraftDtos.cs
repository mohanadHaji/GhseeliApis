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
