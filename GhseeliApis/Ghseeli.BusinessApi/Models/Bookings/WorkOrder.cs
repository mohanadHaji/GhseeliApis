namespace Ghseeli.BusinessApi.Models;

public sealed class WorkOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid AppointmentReservationId { get; set; }
    public AppointmentReservation AppointmentReservation { get; set; } = null!;
    public string Status { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    public string? LicensePlate { get; set; }
    public string? VehicleMake { get; set; }
    public string? VehicleModel { get; set; }
    public string? VehicleColor { get; set; }
    public string AddressLine { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Area { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<WorkOrderItem> Items { get; set; } = new List<WorkOrderItem>();
}

public sealed class WorkOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
    public Guid OfferingId { get; set; }
    public int DisplayOrder { get; set; }
    public decimal BaseSubtotal { get; set; }
    public decimal AddonSubtotal { get; set; }
    public decimal ItemSubtotal { get; set; }
    public int TotalDurationMinutes { get; set; }
    public ICollection<WorkOrderSelection> Selections { get; set; } = new List<WorkOrderSelection>();
}

public sealed class WorkOrderSelection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderItemId { get; set; }
    public WorkOrderItem WorkOrderItem { get; set; } = null!;
    public Guid AddonGroupId { get; set; }
    public Guid AddonChoiceId { get; set; }
    public string SelectionType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceAdjustment { get; set; }
    public decimal TotalPriceAdjustment { get; set; }
    public int UnitDurationAdjustmentMinutes { get; set; }
    public int TotalDurationAdjustmentMinutes { get; set; }
    public bool IsDefaultApplied { get; set; }
    public int DisplayOrder { get; set; }
}
