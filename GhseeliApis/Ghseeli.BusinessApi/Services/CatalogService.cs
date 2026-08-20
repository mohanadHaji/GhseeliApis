using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services.Catalog;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.BusinessApi.Services.Validation.Catalog;

namespace Ghseeli.BusinessApi.Services;

public class CatalogService : ICatalogService
{
    private readonly ICompanyRepository _companyRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICatalogRequestValidator _requestValidator;
    private readonly ICatalogRuleValidator _ruleValidator;

    public CatalogService(
        ICompanyRepository companyRepository,
        ICatalogRepository catalogRepository,
        ICatalogRequestValidator requestValidator,
        ICatalogRuleValidator ruleValidator)
    {
        _companyRepository = companyRepository;
        _catalogRepository = catalogRepository;
        _requestValidator = requestValidator;
        _ruleValidator = ruleValidator;
    }

    public async Task<IReadOnlyCollection<ServiceCategoryListResponse>> GetCategoriesAsync(
        Guid userId,
        bool isAdmin,
        Guid? companyId)
    {
        var company = await ResolveRequestedCompanyAsync(userId, isAdmin, companyId);
        return (await _catalogRepository.GetCategoriesForCompanyAsync(company.Id))
            .Select(CatalogMapper.ToListResponse)
            .ToArray();
    }

    public async Task<ServiceCategoryResponse> GetCategoryAsync(
        Guid userId,
        bool isAdmin,
        Guid categoryId)
    {
        var category = await _catalogRepository.GetCategoryByIdAsync(categoryId)
            ?? throw new KeyNotFoundException("The category was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            category.CompanyId,
            "The category was not found.");
        return CatalogMapper.ToResponse(category);
    }

    public async Task<ServiceCategoryResponse> CreateCategoryAsync(
        Guid userId,
        bool isAdmin,
        CreateServiceCategoryRequest request)
    {
        _requestValidator.Validate(request);

        var company = await ResolveRequestedCompanyAsync(userId, isAdmin, request.CompanyId);
        var category = CatalogMapper.CreateCategory(company, request, DateTime.UtcNow);

        return CatalogMapper.ToResponse(await _catalogRepository.AddCategoryAsync(category));
    }

    public async Task<ServiceCategoryResponse> UpdateCategoryAsync(
        Guid userId,
        bool isAdmin,
        Guid categoryId,
        UpdateServiceCategoryRequest request)
    {
        _requestValidator.Validate(request);

        var category = await _catalogRepository.GetCategoryByIdAsync(categoryId)
            ?? throw new KeyNotFoundException("The category was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            category.CompanyId,
            "The category was not found.");
        CatalogMapper.ApplyCategoryUpdate(category, request, DateTime.UtcNow);

        return CatalogMapper.ToResponse(await _catalogRepository.UpdateCategoryAsync(category));
    }

    public async Task DeleteCategoryAsync(Guid userId, bool isAdmin, Guid categoryId)
    {
        var category = await _catalogRepository.GetCategoryByIdAsync(categoryId)
            ?? throw new KeyNotFoundException("The category was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            category.CompanyId,
            "The category was not found.");
        await _catalogRepository.DeleteCategoryAsync(category);
    }

    public async Task<IReadOnlyCollection<ServiceOfferingListResponse>> GetOfferingsAsync(
        Guid userId,
        bool isAdmin,
        Guid? companyId,
        Guid? categoryId,
        Guid? branchId)
    {
        var company = await ResolveRequestedCompanyAsync(userId, isAdmin, companyId);
        return (await _catalogRepository.GetOfferingsForCompanyAsync(
                company.Id,
                categoryId,
                branchId))
            .Select(CatalogMapper.ToListResponse)
            .ToArray();
    }

    public async Task<ServiceOfferingResponse> GetOfferingAsync(
        Guid userId,
        bool isAdmin,
        Guid offeringId)
    {
        var offering = await _catalogRepository.GetOfferingByIdAsync(offeringId)
            ?? throw new KeyNotFoundException("The offering was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            offering.Category.CompanyId,
            "The offering was not found.");
        return CatalogMapper.ToResponse(offering);
    }

    public async Task<ServiceOfferingResponse> CreateOfferingAsync(
        Guid userId,
        bool isAdmin,
        CreateServiceOfferingRequest request)
    {
        _requestValidator.Validate(request);

        var category = await _catalogRepository.GetCategoryByIdAsync(request.CategoryId)
            ?? throw new KeyNotFoundException("The category was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            category.CompanyId,
            "The category was not found.");
        var branch = await ResolveBranchAsync(
            userId,
            isAdmin,
            category.CompanyId,
            request.BranchId);

        var offering = CatalogMapper.CreateOffering(category, branch, request, DateTime.UtcNow);
        return CatalogMapper.ToResponse(await _catalogRepository.AddOfferingAsync(offering));
    }

    public async Task<ServiceOfferingResponse> UpdateOfferingAsync(
        Guid userId,
        bool isAdmin,
        Guid offeringId,
        UpdateServiceOfferingRequest request)
    {
        _requestValidator.Validate(request);

        var offering = await _catalogRepository.GetOfferingByIdAsync(offeringId)
            ?? throw new KeyNotFoundException("The offering was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            offering.Category.CompanyId,
            "The offering was not found.");
        var branch = await ResolveBranchAsync(
            userId,
            isAdmin,
            offering.Category.CompanyId,
            request.BranchId);

        CatalogMapper.ApplyOfferingUpdate(offering, branch, request, DateTime.UtcNow);
        return CatalogMapper.ToResponse(await _catalogRepository.UpdateOfferingAsync(offering));
    }

    public async Task DeleteOfferingAsync(Guid userId, bool isAdmin, Guid offeringId)
    {
        var offering = await _catalogRepository.GetOfferingByIdAsync(offeringId)
            ?? throw new KeyNotFoundException("The offering was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            offering.Category.CompanyId,
            "The offering was not found.");
        await _catalogRepository.DeleteOfferingAsync(offering);
    }

    public async Task<IReadOnlyCollection<AddonGroupResponse>> GetAddonGroupsAsync(
        Guid userId,
        bool isAdmin,
        Guid offeringId)
    {
        var offering = await _catalogRepository.GetOfferingByIdAsync(offeringId)
            ?? throw new KeyNotFoundException("The offering was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            offering.Category.CompanyId,
            "The offering was not found.");
        return offering.AddonGroups
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.NameAr)
            .Select(CatalogMapper.ToResponse)
            .ToArray();
    }

    public async Task<AddonGroupResponse> GetAddonGroupAsync(
        Guid userId,
        bool isAdmin,
        Guid addonGroupId)
    {
        var addonGroup = await _catalogRepository.GetAddonGroupByIdAsync(addonGroupId)
            ?? throw new KeyNotFoundException("The add-on group was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            addonGroup.ServiceOffering.Category.CompanyId,
            "The add-on group was not found.");
        return CatalogMapper.ToResponse(addonGroup);
    }

    public async Task<AddonGroupResponse> CreateAddonGroupAsync(
        Guid userId,
        bool isAdmin,
        Guid offeringId,
        CreateAddonGroupRequest request)
    {
        _requestValidator.Validate(request);

        var offering = await _catalogRepository.GetOfferingByIdAsync(offeringId)
            ?? throw new KeyNotFoundException("The offering was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            offering.Category.CompanyId,
            "The offering was not found.");

        var addonGroup = CatalogMapper.CreateAddonGroup(offering, request, DateTime.UtcNow);
        _ruleValidator.ValidateAddonGroup(addonGroup);

        return CatalogMapper.ToResponse(await _catalogRepository.AddAddonGroupAsync(addonGroup));
    }

    public async Task<AddonGroupResponse> UpdateAddonGroupAsync(
        Guid userId,
        bool isAdmin,
        Guid addonGroupId,
        UpdateAddonGroupRequest request)
    {
        _requestValidator.Validate(request);

        var addonGroup = await _catalogRepository.GetAddonGroupByIdAsync(addonGroupId)
            ?? throw new KeyNotFoundException("The add-on group was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            addonGroup.ServiceOffering.Category.CompanyId,
            "The add-on group was not found.");

        CatalogMapper.ApplyAddonGroupUpdate(addonGroup, request, DateTime.UtcNow);
        _ruleValidator.ValidateAddonGroup(addonGroup);

        return CatalogMapper.ToResponse(await _catalogRepository.UpdateAddonGroupAsync(addonGroup));
    }

    public async Task DeleteAddonGroupAsync(Guid userId, bool isAdmin, Guid addonGroupId)
    {
        var addonGroup = await _catalogRepository.GetAddonGroupByIdAsync(addonGroupId)
            ?? throw new KeyNotFoundException("The add-on group was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            addonGroup.ServiceOffering.Category.CompanyId,
            "The add-on group was not found.");
        await _catalogRepository.DeleteAddonGroupAsync(addonGroup);
    }

    public async Task<IReadOnlyCollection<AddonChoiceResponse>> GetAddonChoicesAsync(
        Guid userId,
        bool isAdmin,
        Guid addonGroupId)
    {
        var addonGroup = await _catalogRepository.GetAddonGroupByIdAsync(addonGroupId)
            ?? throw new KeyNotFoundException("The add-on group was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            addonGroup.ServiceOffering.Category.CompanyId,
            "The add-on group was not found.");
        return (await _catalogRepository.GetAddonChoicesForGroupAsync(addonGroupId))
            .Select(CatalogMapper.ToResponse)
            .ToArray();
    }

    public async Task<AddonChoiceResponse> GetAddonChoiceAsync(
        Guid userId,
        bool isAdmin,
        Guid addonChoiceId)
    {
        var addonChoice = await _catalogRepository.GetAddonChoiceByIdAsync(addonChoiceId)
            ?? throw new KeyNotFoundException("The add-on choice was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            addonChoice.AddonGroup.ServiceOffering.Category.CompanyId,
            "The add-on choice was not found.");
        return CatalogMapper.ToResponse(addonChoice);
    }

    public async Task<AddonChoiceResponse> CreateAddonChoiceAsync(
        Guid userId,
        bool isAdmin,
        Guid addonGroupId,
        CreateAddonChoiceRequest request)
    {
        _requestValidator.Validate(request);

        var addonGroup = await _catalogRepository.GetAddonGroupByIdAsync(addonGroupId)
            ?? throw new KeyNotFoundException("The add-on group was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            addonGroup.ServiceOffering.Category.CompanyId,
            "The add-on group was not found.");

        var addonChoice = CatalogMapper.CreateAddonChoice(addonGroup, request, DateTime.UtcNow);
        addonGroup.Choices.Add(addonChoice);
        _ruleValidator.ValidateAddonGroup(addonGroup);

        return CatalogMapper.ToResponse(await _catalogRepository.AddAddonChoiceAsync(addonChoice));
    }

    public async Task<AddonChoiceResponse> UpdateAddonChoiceAsync(
        Guid userId,
        bool isAdmin,
        Guid addonChoiceId,
        UpdateAddonChoiceRequest request)
    {
        _requestValidator.Validate(request);

        var addonChoice = await _catalogRepository.GetAddonChoiceByIdAsync(addonChoiceId)
            ?? throw new KeyNotFoundException("The add-on choice was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            addonChoice.AddonGroup.ServiceOffering.Category.CompanyId,
            "The add-on choice was not found.");

        CatalogMapper.ApplyAddonChoiceUpdate(addonChoice, request, DateTime.UtcNow);
        _ruleValidator.ValidateAddonGroup(addonChoice.AddonGroup);

        return CatalogMapper.ToResponse(await _catalogRepository.UpdateAddonChoiceAsync(addonChoice));
    }

    public async Task DeleteAddonChoiceAsync(Guid userId, bool isAdmin, Guid addonChoiceId)
    {
        var addonChoice = await _catalogRepository.GetAddonChoiceByIdAsync(addonChoiceId)
            ?? throw new KeyNotFoundException("The add-on choice was not found.");

        await EnsureCompanyAccessAsync(
            userId,
            isAdmin,
            addonChoice.AddonGroup.ServiceOffering.Category.CompanyId,
            "The add-on choice was not found.");

        addonChoice.AddonGroup.Choices.Remove(addonChoice);
        _ruleValidator.ValidateAddonGroup(addonChoice.AddonGroup);
        await _catalogRepository.DeleteAddonChoiceAsync(addonChoice);
    }

    private async Task<Company> ResolveRequestedCompanyAsync(
        Guid userId,
        bool isAdmin,
        Guid? companyId)
    {
        if (isAdmin)
        {
            if (!companyId.HasValue)
            {
                throw CatalogValidationException.ForField(
                    "companyId",
                    "Admin catalog requests must specify a companyId.");
            }

            return await _companyRepository.GetByIdAsync(companyId.Value)
                ?? throw new KeyNotFoundException("The company was not found.");
        }

        var assignedCompany = await _companyRepository.GetForUserAsync(userId)
            ?? throw new UnauthorizedAccessException(
                "No active company assignment was found for this business account.");

        if (companyId.HasValue && companyId.Value != assignedCompany.Id)
        {
            throw new UnauthorizedAccessException(
                "The requested company is not assigned to this business account.");
        }

        return assignedCompany;
    }

    private async Task EnsureCompanyAccessAsync(
        Guid userId,
        bool isAdmin,
        Guid companyId,
        string missingMessage)
    {
        if (isAdmin)
        {
            return;
        }

        var assignedCompany = await _companyRepository.GetForUserAsync(userId)
            ?? throw new UnauthorizedAccessException(
                "No active company assignment was found for this business account.");
        if (assignedCompany.Id != companyId)
        {
            throw new KeyNotFoundException(missingMessage);
        }
    }

    private async Task<Branch?> ResolveBranchAsync(
        Guid userId,
        bool isAdmin,
        Guid companyId,
        Guid? branchId)
    {
        if (!branchId.HasValue)
        {
            return null;
        }

        var branch = await _companyRepository.GetBranchByIdAsync(branchId.Value)
            ?? throw new KeyNotFoundException("The branch was not found.");

        if (branch.CompanyId == companyId)
        {
            return branch;
        }

        if (!isAdmin)
        {
            throw new KeyNotFoundException("The branch was not found.");
        }

        throw CatalogValidationException.ForField(
            "branchId",
            "The selected branch must belong to the same company as the offering category.");
    }
}
