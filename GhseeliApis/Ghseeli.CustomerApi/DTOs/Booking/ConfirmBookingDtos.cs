namespace GhseeliApis.DTOs.Booking;

using Ghseeli.IntegrationContracts.Vehicles;

public sealed class ConfirmBookingFromDraftRequest
{
    public int ExpectedVersion { get; set; }
    public bool CancellationPolicyAcknowledged { get; set; }
}

public sealed class ConfirmedBookingResponse
{
    public Guid Id { get; set; }
    public Guid ReferenceId { get; set; }
    public Guid OrderGuid { get; set; }
    public Guid BusinessReservationId { get; set; }
    public Guid BusinessWorkOrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DraftVersion { get; set; }
    public string Language { get; set; } = string.Empty;
    public DateTimeOffset RequestedSlotStartUtc { get; set; }
    public DateTimeOffset RequestedSlotEndUtc { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public int TotalDurationMinutes { get; set; }
    public ConfirmedBookingVehicleResponse Vehicle { get; set; } = new();
    public IReadOnlyCollection<ConfirmedBookingItemResponse> Items { get; set; } =
        Array.Empty<ConfirmedBookingItemResponse>();
}

public sealed class ConfirmedBookingVehicleResponse
{
    public VehicleType VehicleType { get; set; }
    public string? ImageUrl { get; set; }
    public string? LicensePlate { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Color { get; set; }
}

public sealed class ConfirmedBookingItemResponse
{
    public Guid OfferingSourceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public decimal ItemSubtotal { get; set; }
    public int TotalDurationMinutes { get; set; }
}
