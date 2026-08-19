using Ghseeli.BusinessApi.Models;

namespace Ghseeli.BusinessApi.DTOs.Catalog;

/// <summary>
/// Creates a localized service category for the owned company.
/// </summary>
public class CreateServiceCategoryRequest
{
    /// <summary>
    /// Target company for admin-managed catalog changes. Owners omit this value.
    /// </summary>
    public Guid? CompanyId { get; set; }

    /// <summary>
    /// Arabic category name stored in the authoritative catalog.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew category name stored in the authoritative catalog.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Optional Arabic description shown to Arabic customers and business users.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew description shown to Hebrew customers and business users.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Non-negative ordering value used when categories are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the category is currently active and eligible for publication.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Updates a localized service category.
/// </summary>
public class UpdateServiceCategoryRequest
{
    /// <summary>
    /// Arabic category name stored in the authoritative catalog.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew category name stored in the authoritative catalog.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Optional Arabic description shown to Arabic customers and business users.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew description shown to Hebrew customers and business users.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Non-negative ordering value used when categories are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the category is currently active and eligible for publication.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Lightweight category data for catalog lists.
/// </summary>
public class ServiceCategoryListResponse
{
    /// <summary>
    /// Category identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Owning company identifier.
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// Arabic category name.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew category name.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Non-negative ordering value used when categories are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the category is active.
    /// </summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// Detailed category data with child offerings.
/// </summary>
public class ServiceCategoryResponse : ServiceCategoryListResponse
{
    /// <summary>
    /// Optional Arabic category description.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew category description.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Offerings currently configured under this category.
    /// </summary>
    public IReadOnlyCollection<ServiceOfferingListResponse> Offerings { get; set; } =
        Array.Empty<ServiceOfferingListResponse>();
}

/// <summary>
/// Creates a localized service offering owned by the business catalog.
/// </summary>
public class CreateServiceOfferingRequest
{
    /// <summary>
    /// Category that owns the offering.
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// Optional branch override. Omit to create a company-wide offering.
    /// </summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// Arabic offering name.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew offering name.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Optional Arabic offering description.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew offering description.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Base offering price before add-on adjustments. Must be non-negative.
    /// </summary>
    public decimal BasePrice { get; set; }

    /// <summary>
    /// Base service duration in minutes. Must be greater than zero.
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// Optional image URL or CDN reference for the offering.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Optional stable business-side reference code for integrations and reports.
    /// </summary>
    public string? ReferenceCode { get; set; }

    /// <summary>
    /// Non-negative ordering value used when offerings are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the offering is currently active and eligible for publication.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Updates a localized service offering.
/// </summary>
public class UpdateServiceOfferingRequest
{
    /// <summary>
    /// Optional branch override. Omit to keep the offering company-wide.
    /// </summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// Arabic offering name.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew offering name.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Optional Arabic offering description.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew offering description.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Base offering price before add-on adjustments. Must be non-negative.
    /// </summary>
    public decimal BasePrice { get; set; }

    /// <summary>
    /// Base service duration in minutes. Must be greater than zero.
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// Optional image URL or CDN reference for the offering.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Optional stable business-side reference code for integrations and reports.
    /// </summary>
    public string? ReferenceCode { get; set; }

    /// <summary>
    /// Non-negative ordering value used when offerings are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the offering is currently active and eligible for publication.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Lightweight offering data for catalog lists.
/// </summary>
public class ServiceOfferingListResponse
{
    /// <summary>
    /// Offering identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Parent category identifier.
    /// </summary>
    public Guid CategoryId { get; set; }

    /// <summary>
    /// Owning company identifier.
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// Optional branch override identifier.
    /// </summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// Arabic offering name.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew offering name.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Base offering price before add-on adjustments.
    /// </summary>
    public decimal BasePrice { get; set; }

    /// <summary>
    /// Base service duration in minutes.
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// Optional image URL or CDN reference.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Optional stable business-side reference code.
    /// </summary>
    public string? ReferenceCode { get; set; }

    /// <summary>
    /// Non-negative ordering value used when offerings are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the offering is active.
    /// </summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// Detailed offering data with localized add-on configuration.
/// </summary>
public class ServiceOfferingResponse : ServiceOfferingListResponse
{
    /// <summary>
    /// Optional Arabic offering description.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew offering description.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Arabic parent category name.
    /// </summary>
    public string CategoryNameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew parent category name.
    /// </summary>
    public string? CategoryNameHe { get; set; }

    /// <summary>
    /// Optional Arabic branch name when the offering is branch-specific.
    /// </summary>
    public string? BranchNameAr { get; set; }

    /// <summary>
    /// Optional Hebrew branch name when the offering is branch-specific.
    /// </summary>
    public string? BranchNameHe { get; set; }

    /// <summary>
    /// Configured add-on groups ordered for business and customer presentation.
    /// </summary>
    public IReadOnlyCollection<AddonGroupResponse> AddonGroups { get; set; } =
        Array.Empty<AddonGroupResponse>();
}

/// <summary>
/// Creates an add-on group and, optionally, its initial choices.
/// </summary>
public class CreateAddonGroupRequest
{
    /// <summary>
    /// Arabic add-on group name.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew add-on group name.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Optional Arabic add-on group description.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew add-on group description.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Selection behavior enforced by the Business API for this group.
    /// </summary>
    public AddonSelectionType SelectionType { get; set; }

    /// <summary>
    /// Whether at least one selection or quantity is required for this group.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Minimum selected choice count, or minimum total quantity for counter groups.
    /// </summary>
    public int MinimumSelections { get; set; }

    /// <summary>
    /// Maximum selected choice count, or maximum total quantity for counter groups.
    /// Single-choice and fixed-included groups must use 1.
    /// </summary>
    public int? MaximumSelections { get; set; }

    /// <summary>
    /// Non-negative ordering value used when add-on groups are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the add-on group is currently active and eligible for publication.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Initial choices to create with the group. Fixed included groups typically supply their required included choice here.
    /// </summary>
    public IReadOnlyCollection<CreateAddonChoiceRequest> Choices { get; set; } =
        Array.Empty<CreateAddonChoiceRequest>();
}

/// <summary>
/// Updates an add-on group and its selection rules.
/// </summary>
public class UpdateAddonGroupRequest
{
    /// <summary>
    /// Arabic add-on group name.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew add-on group name.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Optional Arabic add-on group description.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew add-on group description.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Selection behavior enforced by the Business API for this group.
    /// </summary>
    public AddonSelectionType SelectionType { get; set; }

    /// <summary>
    /// Whether at least one selection or quantity is required for this group.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Minimum selected choice count, or minimum total quantity for counter groups.
    /// </summary>
    public int MinimumSelections { get; set; }

    /// <summary>
    /// Maximum selected choice count, or maximum total quantity for counter groups.
    /// Single-choice and fixed-included groups must use 1.
    /// </summary>
    public int? MaximumSelections { get; set; }

    /// <summary>
    /// Non-negative ordering value used when add-on groups are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the add-on group is currently active and eligible for publication.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Detailed add-on group data with child choices.
/// </summary>
public class AddonGroupResponse
{
    /// <summary>
    /// Add-on group identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Parent offering identifier.
    /// </summary>
    public Guid ServiceOfferingId { get; set; }

    /// <summary>
    /// Arabic add-on group name.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew add-on group name.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Optional Arabic add-on group description.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew add-on group description.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Selection behavior enforced by the Business API for this group.
    /// </summary>
    public AddonSelectionType SelectionType { get; set; }

    /// <summary>
    /// Whether at least one selection or quantity is required for this group.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Minimum selected choice count, or minimum total quantity for counter groups.
    /// </summary>
    public int MinimumSelections { get; set; }

    /// <summary>
    /// Maximum selected choice count, or maximum total quantity for counter groups.
    /// </summary>
    public int? MaximumSelections { get; set; }

    /// <summary>
    /// Non-negative ordering value used when add-on groups are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the add-on group is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Choices configured under the group.
    /// </summary>
    public IReadOnlyCollection<AddonChoiceResponse> Choices { get; set; } =
        Array.Empty<AddonChoiceResponse>();
}

/// <summary>
/// Creates an add-on choice.
/// </summary>
public class CreateAddonChoiceRequest
{
    /// <summary>
    /// Arabic add-on choice name.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew add-on choice name.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Optional Arabic add-on choice description.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew add-on choice description.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Price added to the offering when this choice is selected. Fixed included choices must use zero.
    /// </summary>
    public decimal PriceAdjustment { get; set; }

    /// <summary>
    /// Minutes added to the offering duration when this choice is selected. Fixed included choices must use zero.
    /// </summary>
    public int DurationAdjustmentMinutes { get; set; }

    /// <summary>
    /// Default selected quantity. Use 0 or 1 for single and multiple choice groups, or any non-negative quantity for counter groups.
    /// </summary>
    public int DefaultQuantity { get; set; }

    /// <summary>
    /// Non-negative ordering value used when add-on choices are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the add-on choice is currently active and eligible for publication.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Updates an add-on choice.
/// </summary>
public class UpdateAddonChoiceRequest
{
    /// <summary>
    /// Arabic add-on choice name.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew add-on choice name.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Optional Arabic add-on choice description.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew add-on choice description.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Price added to the offering when this choice is selected. Fixed included choices must use zero.
    /// </summary>
    public decimal PriceAdjustment { get; set; }

    /// <summary>
    /// Minutes added to the offering duration when this choice is selected. Fixed included choices must use zero.
    /// </summary>
    public int DurationAdjustmentMinutes { get; set; }

    /// <summary>
    /// Default selected quantity. Use 0 or 1 for single and multiple choice groups, or any non-negative quantity for counter groups.
    /// </summary>
    public int DefaultQuantity { get; set; }

    /// <summary>
    /// Non-negative ordering value used when add-on choices are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the add-on choice is currently active and eligible for publication.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Detailed add-on choice data.
/// </summary>
public class AddonChoiceResponse
{
    /// <summary>
    /// Add-on choice identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Parent add-on group identifier.
    /// </summary>
    public Guid AddonGroupId { get; set; }

    /// <summary>
    /// Arabic add-on choice name.
    /// </summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew add-on choice name.
    /// </summary>
    public string? NameHe { get; set; }

    /// <summary>
    /// Optional Arabic add-on choice description.
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Optional Hebrew add-on choice description.
    /// </summary>
    public string? DescriptionHe { get; set; }

    /// <summary>
    /// Price added to the offering when this choice is selected.
    /// </summary>
    public decimal PriceAdjustment { get; set; }

    /// <summary>
    /// Minutes added to the offering duration when this choice is selected.
    /// </summary>
    public int DurationAdjustmentMinutes { get; set; }

    /// <summary>
    /// Default selected quantity stored for this choice.
    /// </summary>
    public int DefaultQuantity { get; set; }

    /// <summary>
    /// Non-negative ordering value used when add-on choices are listed.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the add-on choice is active.
    /// </summary>
    public bool IsActive { get; set; }
}
