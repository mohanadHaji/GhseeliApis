using Ghseeli.BusinessApi.Models;

namespace Ghseeli.BusinessApi.Repositories.Interfaces;

public interface ICatalogRepository
{
    Task<IReadOnlyCollection<ServiceCategory>> GetCategoriesForCompanyAsync(Guid companyId);
    Task<ServiceCategory?> GetCategoryByIdAsync(Guid categoryId);
    Task<ServiceCategory> AddCategoryAsync(ServiceCategory category);
    Task<ServiceCategory> UpdateCategoryAsync(ServiceCategory category);
    Task DeleteCategoryAsync(ServiceCategory category);
    Task<IReadOnlyCollection<ServiceOffering>> GetOfferingsForCompanyAsync(
        Guid companyId,
        Guid? categoryId = null,
        Guid? branchId = null);
    Task<ServiceOffering?> GetOfferingByIdAsync(Guid offeringId);
    Task<ServiceOffering> AddOfferingAsync(ServiceOffering offering);
    Task<ServiceOffering> UpdateOfferingAsync(ServiceOffering offering);
    Task DeleteOfferingAsync(ServiceOffering offering);
    Task<AddonGroup?> GetAddonGroupByIdAsync(Guid addonGroupId);
    Task<AddonGroup> AddAddonGroupAsync(AddonGroup addonGroup);
    Task<AddonGroup> UpdateAddonGroupAsync(AddonGroup addonGroup);
    Task DeleteAddonGroupAsync(AddonGroup addonGroup);
    Task<IReadOnlyCollection<AddonChoice>> GetAddonChoicesForGroupAsync(Guid addonGroupId);
    Task<AddonChoice?> GetAddonChoiceByIdAsync(Guid addonChoiceId);
    Task<AddonChoice> AddAddonChoiceAsync(AddonChoice addonChoice);
    Task<AddonChoice> UpdateAddonChoiceAsync(AddonChoice addonChoice);
    Task DeleteAddonChoiceAsync(AddonChoice addonChoice);
}
