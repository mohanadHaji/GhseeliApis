using Ghseeli.BusinessApi.DTOs.Catalog;

namespace Ghseeli.BusinessApi.Services.Interfaces;

public interface ICatalogService
{
    Task<IReadOnlyCollection<ServiceCategoryListResponse>> GetCategoriesAsync(
        Guid userId,
        bool isAdmin,
        Guid? companyId);
    Task<ServiceCategoryResponse> GetCategoryAsync(Guid userId, bool isAdmin, Guid categoryId);
    Task<ServiceCategoryResponse> CreateCategoryAsync(
        Guid userId,
        bool isAdmin,
        CreateServiceCategoryRequest request);
    Task<ServiceCategoryResponse> UpdateCategoryAsync(
        Guid userId,
        bool isAdmin,
        Guid categoryId,
        UpdateServiceCategoryRequest request);
    Task DeleteCategoryAsync(Guid userId, bool isAdmin, Guid categoryId);
    Task<IReadOnlyCollection<ServiceOfferingListResponse>> GetOfferingsAsync(
        Guid userId,
        bool isAdmin,
        Guid? companyId,
        Guid? categoryId,
        Guid? branchId);
    Task<ServiceOfferingResponse> GetOfferingAsync(Guid userId, bool isAdmin, Guid offeringId);
    Task<ServiceOfferingResponse> CreateOfferingAsync(
        Guid userId,
        bool isAdmin,
        CreateServiceOfferingRequest request);
    Task<ServiceOfferingResponse> UpdateOfferingAsync(
        Guid userId,
        bool isAdmin,
        Guid offeringId,
        UpdateServiceOfferingRequest request);
    Task DeleteOfferingAsync(Guid userId, bool isAdmin, Guid offeringId);
    Task<IReadOnlyCollection<AddonGroupResponse>> GetAddonGroupsAsync(
        Guid userId,
        bool isAdmin,
        Guid offeringId);
    Task<AddonGroupResponse> GetAddonGroupAsync(Guid userId, bool isAdmin, Guid addonGroupId);
    Task<AddonGroupResponse> CreateAddonGroupAsync(
        Guid userId,
        bool isAdmin,
        Guid offeringId,
        CreateAddonGroupRequest request);
    Task<AddonGroupResponse> UpdateAddonGroupAsync(
        Guid userId,
        bool isAdmin,
        Guid addonGroupId,
        UpdateAddonGroupRequest request);
    Task DeleteAddonGroupAsync(Guid userId, bool isAdmin, Guid addonGroupId);
    Task<IReadOnlyCollection<AddonChoiceResponse>> GetAddonChoicesAsync(
        Guid userId,
        bool isAdmin,
        Guid addonGroupId);
    Task<AddonChoiceResponse> GetAddonChoiceAsync(Guid userId, bool isAdmin, Guid addonChoiceId);
    Task<AddonChoiceResponse> CreateAddonChoiceAsync(
        Guid userId,
        bool isAdmin,
        Guid addonGroupId,
        CreateAddonChoiceRequest request);
    Task<AddonChoiceResponse> UpdateAddonChoiceAsync(
        Guid userId,
        bool isAdmin,
        Guid addonChoiceId,
        UpdateAddonChoiceRequest request);
    Task DeleteAddonChoiceAsync(Guid userId, bool isAdmin, Guid addonChoiceId);
}
