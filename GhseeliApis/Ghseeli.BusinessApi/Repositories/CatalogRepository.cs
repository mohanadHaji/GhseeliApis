using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.Common.Logging;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Repositories;

public class CatalogRepository : BusinessMutationRepositoryBase, ICatalogRepository
{
    private const string ConflictMessage =
        "The requested business catalog change conflicted with a newer authoritative update. Reload the latest data and retry.";

    private readonly IAppLogger _logger;

    public CatalogRepository(BusinessDbContext context, IAppLogger logger)
        : base(context)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<ServiceCategory>> GetCategoriesForCompanyAsync(
        Guid companyId)
    {
        return await Context.ServiceCategories
            .AsNoTracking()
            .Where(category => category.CompanyId == companyId)
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.NameAr)
            .ToArrayAsync();
    }

    public Task<ServiceCategory?> GetCategoryByIdAsync(Guid categoryId)
    {
        return Context.ServiceCategories
            .Include(category => category.Offerings)
                .ThenInclude(offering => offering.Branch)
            .Include(category => category.Offerings)
                .ThenInclude(offering => offering.AddonGroups)
            .Include(category => category.Company)
            .SingleOrDefaultAsync(category => category.Id == categoryId);
    }

    public async Task<ServiceCategory> AddCategoryAsync(ServiceCategory category)
    {
        Context.ServiceCategories.Add(category);
        await PrepareCompanyVersionIncrementAsync(category.CompanyId);
        await PersistMutationAsync(
            category.CompanyId,
            ConflictMessage,
            allowCompanyOnlyRetry: true);
        _logger.LogInfo(
            $"Catalog category created. CategoryId={category.Id}, CompanyId={category.CompanyId}.");
        return category;
    }

    public async Task<ServiceCategory> UpdateCategoryAsync(ServiceCategory category)
    {
        Context.ServiceCategories.Update(category);
        await PrepareCompanyVersionIncrementAsync(category.CompanyId);
        await PersistMutationAsync(
            category.CompanyId,
            ConflictMessage,
            allowCompanyOnlyRetry: true);
        _logger.LogInfo(
            $"Catalog category updated. CategoryId={category.Id}, CompanyId={category.CompanyId}.");
        return category;
    }

    public async Task DeleteCategoryAsync(ServiceCategory category)
    {
        Context.ServiceCategories.Remove(category);
        await PrepareCompanyVersionIncrementAsync(category.CompanyId);
        await PersistMutationAsync(
            category.CompanyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Catalog category deleted. CategoryId={category.Id}, CompanyId={category.CompanyId}.");
    }

    public async Task<IReadOnlyCollection<ServiceOffering>> GetOfferingsForCompanyAsync(
        Guid companyId,
        Guid? categoryId = null,
        Guid? branchId = null)
    {
        var query = Context.ServiceOfferings
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
        return Context.ServiceOfferings
            .Include(offering => offering.AddonGroups)
                .ThenInclude(group => group.Choices)
            .Include(offering => offering.Category)
                .ThenInclude(category => category.Company)
            .Include(offering => offering.Branch)
            .SingleOrDefaultAsync(offering => offering.Id == offeringId);
    }

    public async Task<ServiceOffering> AddOfferingAsync(ServiceOffering offering)
    {
        Context.ServiceOfferings.Add(offering);
        var companyId = await ResolveCompanyIdAsync(offering);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: true);
        _logger.LogInfo(
            $"Catalog offering created. OfferingId={offering.Id}, CategoryId={offering.CategoryId}, CompanyId={FormatGuid(offering.Category?.CompanyId)}.");
        return offering;
    }

    public async Task<ServiceOffering> UpdateOfferingAsync(ServiceOffering offering)
    {
        Context.ServiceOfferings.Update(offering);
        var companyId = await ResolveCompanyIdAsync(offering);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: true);
        _logger.LogInfo(
            $"Catalog offering updated. OfferingId={offering.Id}, CategoryId={offering.CategoryId}, CompanyId={FormatGuid(offering.Category?.CompanyId)}.");
        return offering;
    }

    public async Task DeleteOfferingAsync(ServiceOffering offering)
    {
        Context.ServiceOfferings.Remove(offering);
        var companyId = await ResolveCompanyIdAsync(offering);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Catalog offering deleted. OfferingId={offering.Id}, CategoryId={offering.CategoryId}, CompanyId={FormatGuid(offering.Category?.CompanyId)}.");
    }

    public Task<AddonGroup?> GetAddonGroupByIdAsync(Guid addonGroupId)
    {
        return Context.AddonGroups
            .Include(group => group.Choices)
            .Include(group => group.ServiceOffering)
                .ThenInclude(offering => offering.Category)
                    .ThenInclude(category => category.Company)
            .SingleOrDefaultAsync(group => group.Id == addonGroupId);
    }

    public async Task<AddonGroup> AddAddonGroupAsync(AddonGroup addonGroup)
    {
        Context.AddonGroups.Add(addonGroup);
        var companyId = await ResolveCompanyIdAsync(addonGroup);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Catalog add-on group created. AddonGroupId={addonGroup.Id}, OfferingId={addonGroup.ServiceOfferingId}, CompanyId={FormatGuid(addonGroup.ServiceOffering?.Category?.CompanyId)}.");
        return addonGroup;
    }

    public async Task<AddonGroup> UpdateAddonGroupAsync(AddonGroup addonGroup)
    {
        Context.AddonGroups.Update(addonGroup);
        var companyId = await ResolveCompanyIdAsync(addonGroup);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Catalog add-on group updated. AddonGroupId={addonGroup.Id}, OfferingId={addonGroup.ServiceOfferingId}, CompanyId={FormatGuid(addonGroup.ServiceOffering?.Category?.CompanyId)}.");
        return addonGroup;
    }

    public async Task DeleteAddonGroupAsync(AddonGroup addonGroup)
    {
        Context.AddonGroups.Remove(addonGroup);
        var companyId = await ResolveCompanyIdAsync(addonGroup);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Catalog add-on group deleted. AddonGroupId={addonGroup.Id}, OfferingId={addonGroup.ServiceOfferingId}, CompanyId={FormatGuid(addonGroup.ServiceOffering?.Category?.CompanyId)}.");
    }

    public async Task<IReadOnlyCollection<AddonChoice>> GetAddonChoicesForGroupAsync(
        Guid addonGroupId)
    {
        return await Context.AddonChoices
            .AsNoTracking()
            .Where(choice => choice.AddonGroupId == addonGroupId)
            .OrderBy(choice => choice.DisplayOrder)
            .ThenBy(choice => choice.NameAr)
            .ToArrayAsync();
    }

    public Task<AddonChoice?> GetAddonChoiceByIdAsync(Guid addonChoiceId)
    {
        return Context.AddonChoices
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
        Context.AddonChoices.Add(addonChoice);
        var companyId = await ResolveCompanyIdAsync(addonChoice);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Catalog add-on choice created. AddonChoiceId={addonChoice.Id}, AddonGroupId={addonChoice.AddonGroupId}, CompanyId={FormatGuid(addonChoice.AddonGroup?.ServiceOffering?.Category?.CompanyId)}.");
        return addonChoice;
    }

    public async Task<AddonChoice> UpdateAddonChoiceAsync(AddonChoice addonChoice)
    {
        Context.AddonChoices.Update(addonChoice);
        var companyId = await ResolveCompanyIdAsync(addonChoice);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Catalog add-on choice updated. AddonChoiceId={addonChoice.Id}, AddonGroupId={addonChoice.AddonGroupId}, CompanyId={FormatGuid(addonChoice.AddonGroup?.ServiceOffering?.Category?.CompanyId)}.");
        return addonChoice;
    }

    public async Task DeleteAddonChoiceAsync(AddonChoice addonChoice)
    {
        Context.AddonChoices.Remove(addonChoice);
        var companyId = await ResolveCompanyIdAsync(addonChoice);
        await PrepareCompanyVersionIncrementAsync(companyId);
        await PersistMutationAsync(
            companyId,
            ConflictMessage,
            allowCompanyOnlyRetry: false);
        _logger.LogInfo(
            $"Catalog add-on choice deleted. AddonChoiceId={addonChoice.Id}, AddonGroupId={addonChoice.AddonGroupId}, CompanyId={FormatGuid(addonChoice.AddonGroup?.ServiceOffering?.Category?.CompanyId)}.");
    }

    private static string FormatGuid(Guid? value)
    {
        return value?.ToString() ?? "unknown";
    }

    private async Task<Guid> ResolveCompanyIdAsync(ServiceOffering offering)
    {
        if (offering.Category is not null)
        {
            return offering.Category.CompanyId;
        }

        return await Context.ServiceCategories
            .Where(category => category.Id == offering.CategoryId)
            .Select(category => category.CompanyId)
            .SingleAsync();
    }

    private async Task<Guid> ResolveCompanyIdAsync(AddonGroup addonGroup)
    {
        if (addonGroup.ServiceOffering?.Category is not null)
        {
            return addonGroup.ServiceOffering.Category.CompanyId;
        }

        return await Context.ServiceOfferings
            .Where(offering => offering.Id == addonGroup.ServiceOfferingId)
            .Select(offering => offering.Category.CompanyId)
            .SingleAsync();
    }

    private async Task<Guid> ResolveCompanyIdAsync(AddonChoice addonChoice)
    {
        if (addonChoice.AddonGroup?.ServiceOffering?.Category is not null)
        {
            return addonChoice.AddonGroup.ServiceOffering.Category.CompanyId;
        }

        return await Context.AddonGroups
            .Where(group => group.Id == addonChoice.AddonGroupId)
            .Select(group => group.ServiceOffering.Category.CompanyId)
            .SingleAsync();
    }
}
