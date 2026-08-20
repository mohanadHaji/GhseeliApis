using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Services;

namespace Ghseeli.BusinessApi.Services.Catalog;

internal static class CatalogMapper
{
    public static ServiceCategory CreateCategory(
        Company company,
        CreateServiceCategoryRequest request,
        DateTime utcNow)
    {
        return new ServiceCategory
        {
            CompanyId = company.Id,
            Company = company,
            NameAr = BusinessTextNormalizer.NormalizeRequired(request.NameAr),
            NameHe = BusinessTextNormalizer.NormalizeOptional(request.NameHe),
            DescriptionAr = BusinessTextNormalizer.NormalizeOptional(request.DescriptionAr),
            DescriptionHe = BusinessTextNormalizer.NormalizeOptional(request.DescriptionHe),
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            CreatedAt = utcNow
        };
    }

    public static void ApplyCategoryUpdate(
        ServiceCategory category,
        UpdateServiceCategoryRequest request,
        DateTime utcNow)
    {
        category.NameAr = BusinessTextNormalizer.NormalizeRequired(request.NameAr);
        category.NameHe = BusinessTextNormalizer.NormalizeOptional(request.NameHe);
        category.DescriptionAr = BusinessTextNormalizer.NormalizeOptional(request.DescriptionAr);
        category.DescriptionHe = BusinessTextNormalizer.NormalizeOptional(request.DescriptionHe);
        category.DisplayOrder = request.DisplayOrder;
        category.IsActive = request.IsActive;
        category.UpdatedAt = utcNow;
    }

    public static ServiceOffering CreateOffering(
        ServiceCategory category,
        Branch? branch,
        CreateServiceOfferingRequest request,
        DateTime utcNow)
    {
        return new ServiceOffering
        {
            CategoryId = category.Id,
            Category = category,
            BranchId = branch?.Id,
            Branch = branch,
            NameAr = BusinessTextNormalizer.NormalizeRequired(request.NameAr),
            NameHe = BusinessTextNormalizer.NormalizeOptional(request.NameHe),
            DescriptionAr = BusinessTextNormalizer.NormalizeOptional(request.DescriptionAr),
            DescriptionHe = BusinessTextNormalizer.NormalizeOptional(request.DescriptionHe),
            BasePrice = BusinessMoney.RoundToCurrency(request.BasePrice),
            DurationMinutes = request.DurationMinutes,
            ImageUrl = BusinessTextNormalizer.NormalizeOptional(request.ImageUrl),
            ReferenceCode = BusinessTextNormalizer.NormalizeOptional(request.ReferenceCode),
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            CreatedAt = utcNow
        };
    }

    public static void ApplyOfferingUpdate(
        ServiceOffering offering,
        Branch? branch,
        UpdateServiceOfferingRequest request,
        DateTime utcNow)
    {
        offering.BranchId = branch?.Id;
        offering.Branch = branch;
        offering.NameAr = BusinessTextNormalizer.NormalizeRequired(request.NameAr);
        offering.NameHe = BusinessTextNormalizer.NormalizeOptional(request.NameHe);
        offering.DescriptionAr = BusinessTextNormalizer.NormalizeOptional(request.DescriptionAr);
        offering.DescriptionHe = BusinessTextNormalizer.NormalizeOptional(request.DescriptionHe);
        offering.BasePrice = BusinessMoney.RoundToCurrency(request.BasePrice);
        offering.DurationMinutes = request.DurationMinutes;
        offering.ImageUrl = BusinessTextNormalizer.NormalizeOptional(request.ImageUrl);
        offering.ReferenceCode = BusinessTextNormalizer.NormalizeOptional(request.ReferenceCode);
        offering.DisplayOrder = request.DisplayOrder;
        offering.IsActive = request.IsActive;
        offering.UpdatedAt = utcNow;
    }

    public static AddonGroup CreateAddonGroup(
        ServiceOffering offering,
        CreateAddonGroupRequest request,
        DateTime utcNow)
    {
        return new AddonGroup
        {
            ServiceOfferingId = offering.Id,
            ServiceOffering = offering,
            NameAr = BusinessTextNormalizer.NormalizeRequired(request.NameAr),
            NameHe = BusinessTextNormalizer.NormalizeOptional(request.NameHe),
            DescriptionAr = BusinessTextNormalizer.NormalizeOptional(request.DescriptionAr),
            DescriptionHe = BusinessTextNormalizer.NormalizeOptional(request.DescriptionHe),
            SelectionType = request.SelectionType,
            IsRequired = request.IsRequired,
            MinimumSelections = request.MinimumSelections,
            MaximumSelections = request.MaximumSelections,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            CreatedAt = utcNow,
            Choices = (request.Choices ?? Array.Empty<CreateAddonChoiceRequest>())
                .Select(choice => CreateAddonChoice(choice, utcNow))
                .ToList()
        };
    }

    public static void ApplyAddonGroupUpdate(
        AddonGroup addonGroup,
        UpdateAddonGroupRequest request,
        DateTime utcNow)
    {
        addonGroup.NameAr = BusinessTextNormalizer.NormalizeRequired(request.NameAr);
        addonGroup.NameHe = BusinessTextNormalizer.NormalizeOptional(request.NameHe);
        addonGroup.DescriptionAr = BusinessTextNormalizer.NormalizeOptional(request.DescriptionAr);
        addonGroup.DescriptionHe = BusinessTextNormalizer.NormalizeOptional(request.DescriptionHe);
        addonGroup.SelectionType = request.SelectionType;
        addonGroup.IsRequired = request.IsRequired;
        addonGroup.MinimumSelections = request.MinimumSelections;
        addonGroup.MaximumSelections = request.MaximumSelections;
        addonGroup.DisplayOrder = request.DisplayOrder;
        addonGroup.IsActive = request.IsActive;
        addonGroup.UpdatedAt = utcNow;
    }

    public static AddonChoice CreateAddonChoice(
        AddonGroup addonGroup,
        CreateAddonChoiceRequest request,
        DateTime utcNow)
    {
        var addonChoice = CreateAddonChoice(request, utcNow);
        addonChoice.AddonGroupId = addonGroup.Id;
        addonChoice.AddonGroup = addonGroup;
        return addonChoice;
    }

    public static void ApplyAddonChoiceUpdate(
        AddonChoice addonChoice,
        UpdateAddonChoiceRequest request,
        DateTime utcNow)
    {
        addonChoice.NameAr = BusinessTextNormalizer.NormalizeRequired(request.NameAr);
        addonChoice.NameHe = BusinessTextNormalizer.NormalizeOptional(request.NameHe);
        addonChoice.DescriptionAr = BusinessTextNormalizer.NormalizeOptional(request.DescriptionAr);
        addonChoice.DescriptionHe = BusinessTextNormalizer.NormalizeOptional(request.DescriptionHe);
        addonChoice.PriceAdjustment = BusinessMoney.RoundToCurrency(request.PriceAdjustment);
        addonChoice.DurationAdjustmentMinutes = request.DurationAdjustmentMinutes;
        addonChoice.DefaultQuantity = request.DefaultQuantity;
        addonChoice.DisplayOrder = request.DisplayOrder;
        addonChoice.IsActive = request.IsActive;
        addonChoice.UpdatedAt = utcNow;
    }

    public static ServiceCategoryListResponse ToListResponse(ServiceCategory category)
    {
        return new ServiceCategoryListResponse
        {
            Id = category.Id,
            CompanyId = category.CompanyId,
            NameAr = category.NameAr,
            NameHe = category.NameHe,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive
        };
    }

    public static ServiceCategoryResponse ToResponse(ServiceCategory category)
    {
        return new ServiceCategoryResponse
        {
            Id = category.Id,
            CompanyId = category.CompanyId,
            NameAr = category.NameAr,
            NameHe = category.NameHe,
            DescriptionAr = category.DescriptionAr,
            DescriptionHe = category.DescriptionHe,
            DisplayOrder = category.DisplayOrder,
            IsActive = category.IsActive,
            Offerings = category.Offerings
                .OrderBy(offering => offering.DisplayOrder)
                .ThenBy(offering => offering.NameAr)
                .Select(ToListResponse)
                .ToArray()
        };
    }

    public static ServiceOfferingListResponse ToListResponse(ServiceOffering offering)
    {
        return new ServiceOfferingListResponse
        {
            Id = offering.Id,
            CategoryId = offering.CategoryId,
            CompanyId = offering.Category.CompanyId,
            BranchId = offering.BranchId,
            NameAr = offering.NameAr,
            NameHe = offering.NameHe,
            BasePrice = offering.BasePrice,
            DurationMinutes = offering.DurationMinutes,
            ImageUrl = offering.ImageUrl,
            ReferenceCode = offering.ReferenceCode,
            DisplayOrder = offering.DisplayOrder,
            IsActive = offering.IsActive
        };
    }

    public static ServiceOfferingResponse ToResponse(ServiceOffering offering)
    {
        return new ServiceOfferingResponse
        {
            Id = offering.Id,
            CategoryId = offering.CategoryId,
            CompanyId = offering.Category.CompanyId,
            BranchId = offering.BranchId,
            NameAr = offering.NameAr,
            NameHe = offering.NameHe,
            DescriptionAr = offering.DescriptionAr,
            DescriptionHe = offering.DescriptionHe,
            BasePrice = offering.BasePrice,
            DurationMinutes = offering.DurationMinutes,
            ImageUrl = offering.ImageUrl,
            ReferenceCode = offering.ReferenceCode,
            DisplayOrder = offering.DisplayOrder,
            IsActive = offering.IsActive,
            CategoryNameAr = offering.Category.NameAr,
            CategoryNameHe = offering.Category.NameHe,
            BranchNameAr = offering.Branch?.NameAr,
            BranchNameHe = offering.Branch?.NameHe,
            AddonGroups = offering.AddonGroups
                .OrderBy(group => group.DisplayOrder)
                .ThenBy(group => group.NameAr)
                .Select(ToResponse)
                .ToArray()
        };
    }

    public static AddonGroupResponse ToResponse(AddonGroup addonGroup)
    {
        return new AddonGroupResponse
        {
            Id = addonGroup.Id,
            ServiceOfferingId = addonGroup.ServiceOfferingId,
            NameAr = addonGroup.NameAr,
            NameHe = addonGroup.NameHe,
            DescriptionAr = addonGroup.DescriptionAr,
            DescriptionHe = addonGroup.DescriptionHe,
            SelectionType = addonGroup.SelectionType,
            IsRequired = addonGroup.IsRequired,
            MinimumSelections = addonGroup.MinimumSelections,
            MaximumSelections = addonGroup.MaximumSelections,
            DisplayOrder = addonGroup.DisplayOrder,
            IsActive = addonGroup.IsActive,
            Choices = addonGroup.Choices
                .OrderBy(choice => choice.DisplayOrder)
                .ThenBy(choice => choice.NameAr)
                .Select(ToResponse)
                .ToArray()
        };
    }

    public static AddonChoiceResponse ToResponse(AddonChoice addonChoice)
    {
        return new AddonChoiceResponse
        {
            Id = addonChoice.Id,
            AddonGroupId = addonChoice.AddonGroupId,
            NameAr = addonChoice.NameAr,
            NameHe = addonChoice.NameHe,
            DescriptionAr = addonChoice.DescriptionAr,
            DescriptionHe = addonChoice.DescriptionHe,
            PriceAdjustment = addonChoice.PriceAdjustment,
            DurationAdjustmentMinutes = addonChoice.DurationAdjustmentMinutes,
            DefaultQuantity = addonChoice.DefaultQuantity,
            DisplayOrder = addonChoice.DisplayOrder,
            IsActive = addonChoice.IsActive
        };
    }

    private static AddonChoice CreateAddonChoice(
        CreateAddonChoiceRequest request,
        DateTime utcNow)
    {
        return new AddonChoice
        {
            NameAr = BusinessTextNormalizer.NormalizeRequired(request.NameAr),
            NameHe = BusinessTextNormalizer.NormalizeOptional(request.NameHe),
            DescriptionAr = BusinessTextNormalizer.NormalizeOptional(request.DescriptionAr),
            DescriptionHe = BusinessTextNormalizer.NormalizeOptional(request.DescriptionHe),
            PriceAdjustment = BusinessMoney.RoundToCurrency(request.PriceAdjustment),
            DurationAdjustmentMinutes = request.DurationAdjustmentMinutes,
            DefaultQuantity = request.DefaultQuantity,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            CreatedAt = utcNow
        };
    }
}
