using Ghseeli.IntegrationContracts.InternalHttp;

namespace Ghseeli.IntegrationContracts.BusinessCatalog;

public static class AppointmentValidationErrorCodes
{
    public const string StaleCatalogVersion = "STALE_CATALOG_VERSION";
    public const string UnsupportedCurrency = "UNSUPPORTED_CURRENCY";
    public const string CompanyInactive = "COMPANY_INACTIVE";
    public const string BranchNotFound = "BRANCH_NOT_FOUND";
    public const string BranchInactive = "BRANCH_INACTIVE";
    public const string OfferingNotFound = "OFFERING_NOT_FOUND";
    public const string OfferingInactive = "OFFERING_INACTIVE";
    public const string OfferingBranchMismatch = "OFFERING_BRANCH_MISMATCH";
    public const string CategoryInactive = "CATEGORY_INACTIVE";
    public const string DuplicateAddonChoice = "DUPLICATE_ADDON_CHOICE";
    public const string UnknownAddonChoice = "UNKNOWN_ADDON_CHOICE";
    public const string InactiveAddonChoice = "INACTIVE_ADDON_CHOICE";
    public const string AddonSelectionRuleViolation = "ADDON_SELECTION_RULE_VIOLATION";
    public const string ServiceAreaNotConfigured = "SERVICE_AREA_NOT_CONFIGURED";
    public const string CustomerLocationRequired = "CUSTOMER_LOCATION_REQUIRED";
    public const string OutOfServiceArea = "OUT_OF_SERVICE_AREA";
    public const string AvailabilityNotConfigured = "AVAILABILITY_NOT_CONFIGURED";
    public const string AvailabilityInactive = "AVAILABILITY_INACTIVE";
    public const string InvalidTimeZone = "INVALID_TIME_ZONE";
    public const string SlotBeforeLeadTime = "SLOT_BEFORE_LEAD_TIME";
    public const string SlotBeyondHorizon = "SLOT_BEYOND_HORIZON";
    public const string SlotUnavailable = "SLOT_UNAVAILABLE";
    public const string SlotMisaligned = "SLOT_MISALIGNED";
}

public sealed class CatalogSnapshotResponse
{
    public string ContractVersion { get; set; } = BusinessCatalogContract.Version;
    public long CatalogVersion { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public CatalogSnapshotCompany Company { get; set; } = new();
    public IReadOnlyCollection<CatalogSnapshotBranch> Branches { get; set; } =
        Array.Empty<CatalogSnapshotBranch>();
    public IReadOnlyCollection<CatalogSnapshotServiceArea> ServiceAreas { get; set; } =
        Array.Empty<CatalogSnapshotServiceArea>();
    public IReadOnlyCollection<CatalogSnapshotCategory> Categories { get; set; } =
        Array.Empty<CatalogSnapshotCategory>();
}

public sealed class CatalogSnapshotCompany
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public string? Phone { get; set; }
}

public sealed class CatalogSnapshotBranch
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string AddressAr { get; set; } = string.Empty;
    public string? AddressHe { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public CatalogSnapshotBranchAvailability? Availability { get; set; }
}

public sealed class CatalogSnapshotBranchAvailability
{
    public bool IsActive { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public int MinimumLeadMinutes { get; set; }
    public int BookingHorizonDays { get; set; }
    public IReadOnlyCollection<CatalogSnapshotRecurringSchedule> RecurringSchedules { get; set; } =
        Array.Empty<CatalogSnapshotRecurringSchedule>();
    public IReadOnlyCollection<CatalogSnapshotAvailabilityOverride> AvailabilityOverrides { get; set; } =
        Array.Empty<CatalogSnapshotAvailabilityOverride>();
}

public sealed class CatalogSnapshotRecurringSchedule
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartLocalTime { get; set; }
    public TimeSpan EndLocalTime { get; set; }
    public int SlotDurationMinutes { get; set; }
    public int Capacity { get; set; }
}

public sealed class CatalogSnapshotAvailabilityOverride
{
    public DateOnly OverrideDate { get; set; }
    public bool IsClosed { get; set; }
    public TimeSpan? StartLocalTime { get; set; }
    public TimeSpan? EndLocalTime { get; set; }
    public int? SlotDurationMinutes { get; set; }
    public int? Capacity { get; set; }
}

public sealed class CatalogSnapshotServiceArea
{
    public Guid BranchId { get; set; }
    public bool IsActive { get; set; }
    public bool UsesBranchCoordinates { get; set; }
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double RadiusKm { get; set; }
}

public sealed class CatalogSnapshotCategory
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public int DisplayOrder { get; set; }
    public IReadOnlyCollection<CatalogSnapshotOffering> Offerings { get; set; } =
        Array.Empty<CatalogSnapshotOffering>();
}

public sealed class CatalogSnapshotOffering
{
    public Guid Id { get; set; }
    public Guid? BranchId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public decimal BasePrice { get; set; }
    public int DurationMinutes { get; set; }
    public string? ImageUrl { get; set; }
    public string? ReferenceCode { get; set; }
    public int DisplayOrder { get; set; }
    public IReadOnlyCollection<CatalogSnapshotAddonGroup> AddonGroups { get; set; } =
        Array.Empty<CatalogSnapshotAddonGroup>();
}

public sealed class CatalogSnapshotAddonGroup
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public string SelectionType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public int MinimumSelections { get; set; }
    public int? MaximumSelections { get; set; }
    public int DisplayOrder { get; set; }
    public IReadOnlyCollection<CatalogSnapshotAddonChoice> Choices { get; set; } =
        Array.Empty<CatalogSnapshotAddonChoice>();
}

public sealed class CatalogSnapshotAddonChoice
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public decimal PriceAdjustment { get; set; }
    public int DurationAdjustmentMinutes { get; set; }
    public int DefaultQuantity { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class ValidateAppointmentRequest
{
    public string ContractVersion { get; set; } = BusinessCatalogContract.Version;
    public Guid BranchId { get; set; }
    public Guid OfferingId { get; set; }
    public IReadOnlyCollection<ValidateAppointmentAddonSelectionRequest> SelectedAddons { get; set; } =
        Array.Empty<ValidateAppointmentAddonSelectionRequest>();
    public DateTimeOffset RequestedSlotStartUtc { get; set; }
    public AppointmentCustomerLocationFacts? CustomerLocation { get; set; }
    public long? ExpectedCatalogVersion { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public sealed class ValidateAppointmentAddonSelectionRequest
{
    public Guid AddonChoiceId { get; set; }
    public int Quantity { get; set; }
}

public sealed class AppointmentCustomerLocationFacts
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public sealed class ValidateAppointmentResponse
{
    public string ContractVersion { get; set; } = BusinessCatalogContract.Version;
    public bool Valid { get; set; }
    public long CatalogVersion { get; set; }
    public string Currency { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public Guid OfferingId { get; set; }
    public IReadOnlyCollection<AppointmentValidationIssue> Errors { get; set; } =
        Array.Empty<AppointmentValidationIssue>();
    public IReadOnlyCollection<NormalizedAddonSelection> NormalizedSelections { get; set; } =
        Array.Empty<NormalizedAddonSelection>();
    public decimal BaseSubtotal { get; set; }
    public decimal AddonSubtotal { get; set; }
    public decimal TotalPrice { get; set; }
    public int TotalDurationMinutes { get; set; }
    public AppointmentAvailabilityFacts Availability { get; set; } = new();
    public AppointmentServiceAreaFacts ServiceArea { get; set; } = new();
}

public sealed class AppointmentValidationIssue
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Field { get; set; }
}

public sealed class NormalizedAddonSelection
{
    public Guid AddonGroupId { get; set; }
    public Guid AddonChoiceId { get; set; }
    public string SelectionType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceAdjustment { get; set; }
    public decimal TotalPriceAdjustment { get; set; }
    public int UnitDurationAdjustmentMinutes { get; set; }
    public int TotalDurationAdjustmentMinutes { get; set; }
    public bool IsDefaultApplied { get; set; }
}

public sealed class AppointmentAvailabilityFacts
{
    public bool IsAvailable { get; set; }
    public bool HasActiveConfiguration { get; set; }
    public bool UsedDateOverride { get; set; }
    public bool CapacityReservationChecked { get; set; }
    public string? TimeZoneId { get; set; }
    public string WindowSource { get; set; } = string.Empty;
    public DateTime RequestedSlotStartUtc { get; set; }
    public DateTime RequestedSlotEndUtc { get; set; }
    public int? SlotDurationMinutes { get; set; }
    public int? ConfiguredCapacity { get; set; }
}

public sealed class AppointmentServiceAreaFacts
{
    public bool ServiceAreaConfigured { get; set; }
    public bool CustomerLocationRequired { get; set; }
    public bool IsWithinServiceArea { get; set; }
    public bool UsedBranchCoordinates { get; set; }
    public double? DistanceKm { get; set; }
    public double? RadiusKm { get; set; }
    public double? EffectiveCenterLatitude { get; set; }
    public double? EffectiveCenterLongitude { get; set; }
}
