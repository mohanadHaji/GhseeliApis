using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.Common.Logging;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Repositories;

public class CatalogRepository : ICatalogRepository
{
    private readonly BusinessDbContext _context;
    private readonly IAppLogger _logger;

    public CatalogRepository(BusinessDbContext context, IAppLogger logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<ServiceCategory>> GetCategoriesForCompanyAsync(
        Guid companyId)
    {
        return await _context.ServiceCategories
            .AsNoTracking()
            .Where(category => category.CompanyId == companyId)
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.NameAr)
            .ToArrayAsync();
    }

    public Task<ServiceCategory?> GetCategoryByIdAsync(Guid categoryId)
    {
        return _context.ServiceCategories
            .Include(category => category.Offerings)
                .ThenInclude(offering => offering.Branch)
            .Include(category => category.Offerings)
                .ThenInclude(offering => offering.AddonGroups)
            .Include(category => category.Company)
            .SingleOrDefaultAsync(category => category.Id == categoryId);
    }

    public async Task<ServiceCategory> AddCategoryAsync(ServiceCategory category)
    {
        _context.ServiceCategories.Add(category);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog category created. CategoryId={category.Id}, CompanyId={category.CompanyId}.");
        return category;
    }

    public async Task<ServiceCategory> UpdateCategoryAsync(ServiceCategory category)
    {
        _context.ServiceCategories.Update(category);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog category updated. CategoryId={category.Id}, CompanyId={category.CompanyId}.");
        return category;
    }

    public async Task DeleteCategoryAsync(ServiceCategory category)
    {
        _context.ServiceCategories.Remove(category);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog category deleted. CategoryId={category.Id}, CompanyId={category.CompanyId}.");
    }

    public async Task<IReadOnlyCollection<ServiceOffering>> GetOfferingsForCompanyAsync(
        Guid companyId,
        Guid? categoryId = null,
        Guid? branchId = null)
    {
        var query = _context.ServiceOfferings
            .AsNoTracking()
            .Include(offering => offering.Category)
            .Include(offering => offering.Branch)
            .Where(offering => offering.Category.CompanyId == companyId);

        if (categoryId.HasValue)
        {
            query = query.Where(offering => offering.CategoryId == categoryId.Value);
        }

        if (branchId.HasValue)
        {
            query = query.Where(offering => offering.BranchId == branchId.Value);
        }

        return await query
            .OrderBy(offering => offering.DisplayOrder)
            .ThenBy(offering => offering.NameAr)
            .ToArrayAsync();
    }

    public Task<ServiceOffering?> GetOfferingByIdAsync(Guid offeringId)
    {
        return _context.ServiceOfferings
            .Include(offering => offering.AddonGroups)
                .ThenInclude(group => group.Choices)
            .Include(offering => offering.Category)
                .ThenInclude(category => category.Company)
            .Include(offering => offering.Branch)
            .SingleOrDefaultAsync(offering => offering.Id == offeringId);
    }

    public async Task<ServiceOffering> AddOfferingAsync(ServiceOffering offering)
    {
        _context.ServiceOfferings.Add(offering);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog offering created. OfferingId={offering.Id}, CategoryId={offering.CategoryId}, CompanyId={FormatGuid(offering.Category?.CompanyId)}.");
        return offering;
    }

    public async Task<ServiceOffering> UpdateOfferingAsync(ServiceOffering offering)
    {
        _context.ServiceOfferings.Update(offering);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog offering updated. OfferingId={offering.Id}, CategoryId={offering.CategoryId}, CompanyId={FormatGuid(offering.Category?.CompanyId)}.");
        return offering;
    }

    public async Task DeleteOfferingAsync(ServiceOffering offering)
    {
        _context.ServiceOfferings.Remove(offering);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog offering deleted. OfferingId={offering.Id}, CategoryId={offering.CategoryId}, CompanyId={FormatGuid(offering.Category?.CompanyId)}.");
    }

    public Task<AddonGroup?> GetAddonGroupByIdAsync(Guid addonGroupId)
    {
        return _context.AddonGroups
            .Include(group => group.Choices)
            .Include(group => group.ServiceOffering)
                .ThenInclude(offering => offering.Category)
                    .ThenInclude(category => category.Company)
            .SingleOrDefaultAsync(group => group.Id == addonGroupId);
    }

    public async Task<AddonGroup> AddAddonGroupAsync(AddonGroup addonGroup)
    {
        _context.AddonGroups.Add(addonGroup);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog add-on group created. AddonGroupId={addonGroup.Id}, OfferingId={addonGroup.ServiceOfferingId}, CompanyId={FormatGuid(addonGroup.ServiceOffering?.Category?.CompanyId)}.");
        return addonGroup;
    }

    public async Task<AddonGroup> UpdateAddonGroupAsync(AddonGroup addonGroup)
    {
        _context.AddonGroups.Update(addonGroup);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog add-on group updated. AddonGroupId={addonGroup.Id}, OfferingId={addonGroup.ServiceOfferingId}, CompanyId={FormatGuid(addonGroup.ServiceOffering?.Category?.CompanyId)}.");
        return addonGroup;
    }

    public async Task DeleteAddonGroupAsync(AddonGroup addonGroup)
    {
        _context.AddonGroups.Remove(addonGroup);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog add-on group deleted. AddonGroupId={addonGroup.Id}, OfferingId={addonGroup.ServiceOfferingId}, CompanyId={FormatGuid(addonGroup.ServiceOffering?.Category?.CompanyId)}.");
    }

    public async Task<IReadOnlyCollection<AddonChoice>> GetAddonChoicesForGroupAsync(
        Guid addonGroupId)
    {
        return await _context.AddonChoices
            .AsNoTracking()
            .Where(choice => choice.AddonGroupId == addonGroupId)
            .OrderBy(choice => choice.DisplayOrder)
            .ThenBy(choice => choice.NameAr)
            .ToArrayAsync();
    }

    public Task<AddonChoice?> GetAddonChoiceByIdAsync(Guid addonChoiceId)
    {
        return _context.AddonChoices
            .Include(choice => choice.AddonGroup)
                .ThenInclude(group => group.Choices)
            .Include(choice => choice.AddonGroup)
                .ThenInclude(group => group.ServiceOffering)
                    .ThenInclude(offering => offering.Category)
                        .ThenInclude(category => category.Company)
            .SingleOrDefaultAsync(choice => choice.Id == addonChoiceId);
    }

    public async Task<AddonChoice> AddAddonChoiceAsync(AddonChoice addonChoice)
    {
        _context.AddonChoices.Add(addonChoice);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog add-on choice created. AddonChoiceId={addonChoice.Id}, AddonGroupId={addonChoice.AddonGroupId}, CompanyId={FormatGuid(addonChoice.AddonGroup?.ServiceOffering?.Category?.CompanyId)}.");
        return addonChoice;
    }

    public async Task<AddonChoice> UpdateAddonChoiceAsync(AddonChoice addonChoice)
    {
        _context.AddonChoices.Update(addonChoice);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog add-on choice updated. AddonChoiceId={addonChoice.Id}, AddonGroupId={addonChoice.AddonGroupId}, CompanyId={FormatGuid(addonChoice.AddonGroup?.ServiceOffering?.Category?.CompanyId)}.");
        return addonChoice;
    }

    public async Task DeleteAddonChoiceAsync(AddonChoice addonChoice)
    {
        _context.AddonChoices.Remove(addonChoice);
        await _context.SaveChangesAsync();
        _logger.LogInfo(
            $"Catalog add-on choice deleted. AddonChoiceId={addonChoice.Id}, AddonGroupId={addonChoice.AddonGroupId}, CompanyId={FormatGuid(addonChoice.AddonGroup?.ServiceOffering?.Category?.CompanyId)}.");
    }

    private static string FormatGuid(Guid? value)
    {
        return value?.ToString() ?? "unknown";
    }
}
