namespace GhseeliApis.Services.Checkout;

public static class CheckoutDraftProblemCodes
{
    public const string Invalid = "checkout_draft_invalid";
    public const string SelectionInvalid = "checkout_draft_selection_invalid";
    public const string SlotUnavailable = "checkout_draft_slot_unavailable";
    public const string OutOfServiceArea = "checkout_draft_out_of_service_area";
    public const string NotFound = "checkout_draft_not_found";
    public const string Expired = "checkout_draft_expired";
    public const string VersionConflict = "checkout_draft_version_conflict";
}

internal static class CheckoutDraftFieldErrorCodes
{
    public const string Required = "checkout_required";
    public const string GuidRequired = "checkout_guid_required";
    public const string MaxLength = "checkout_max_length";
    public const string CoordinateFinite = "checkout_coordinate_finite";
    public const string CoordinateRange = "checkout_coordinate_range";
    public const string RequestedSlotRequired = "checkout_requested_slot_required";
    public const string CollectionRequired = "checkout_collection_required";
    public const string CollectionTooMany = "checkout_collection_too_many";
    public const string DuplicateOffering = "checkout_duplicate_offering";
    public const string DuplicateAddonChoice = "checkout_duplicate_addon_choice";
    public const string SelectionQuantityRange = "checkout_selection_quantity_range";
    public const string ExpectedVersionInvalid = "checkout_expected_version_invalid";
}
