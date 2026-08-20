using FluentValidation;
using FluentValidation.Results;
using Ghseeli.BusinessApi.DTOs.Companies;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.BusinessApi.Services.Validation.Companies;
using Ghseeli.Common.Logging;

namespace Ghseeli.BusinessApi.Services;

public class CompanyProfileService : ICompanyProfileService
{
    private readonly ICompanyRepository _repository;
    private readonly ICompanyRequestValidator _requestValidator;
    private readonly IAppLogger _logger;

    public CompanyProfileService(
        ICompanyRepository repository,
        ICompanyRequestValidator requestValidator,
        IAppLogger logger)
    {
        _repository = repository;
        _requestValidator = requestValidator;
        _logger = logger;
    }

    public async Task<CompanyProfileResponse> GetMyCompanyAsync(Guid userId)
    {
        var company = await GetAssignedCompanyAsync(userId, "GetMyCompany");
        return MapCompany(company);
    }

    public async Task<CompanyProfileResponse> UpdateMyCompanyAsync(
        Guid userId,
        UpdateCompanyProfileRequest request)
    {
        _requestValidator.Validate(request);

        var company = await GetAssignedCompanyAsync(userId, "UpdateMyCompany");
        company.NameAr = BusinessTextNormalizer.NormalizeRequired(request.NameAr);
        company.NameHe = BusinessTextNormalizer.NormalizeOptional(request.NameHe);
        company.DescriptionAr = BusinessTextNormalizer.NormalizeOptional(request.DescriptionAr);
        company.DescriptionHe = BusinessTextNormalizer.NormalizeOptional(request.DescriptionHe);
        company.ServiceAreaDescriptionAr = BusinessTextNormalizer.NormalizeOptional(request.ServiceAreaDescriptionAr);
        company.ServiceAreaDescriptionHe = BusinessTextNormalizer.NormalizeOptional(request.ServiceAreaDescriptionHe);
        company.Phone = BusinessTextNormalizer.NormalizeOptional(request.Phone);
        company.UpdatedAt = DateTime.UtcNow;

        var updatedCompany = await _repository.UpdateAsync(company);
        _logger.LogInfo(
            $"Business company profile updated for company {updatedCompany.Id} by user {userId}.");
        return MapCompany(updatedCompany);
    }

    public async Task<BranchResponse> CreateBranchAsync(
        Guid userId,
        CreateBranchRequest request)
    {
        _requestValidator.Validate(request);

        var company = await GetAssignedCompanyAsync(userId, "CreateBranch");
        var branch = new Branch
        {
            CompanyId = company.Id,
            NameAr = BusinessTextNormalizer.NormalizeRequired(request.NameAr),
            NameHe = BusinessTextNormalizer.NormalizeOptional(request.NameHe),
            AddressAr = BusinessTextNormalizer.NormalizeRequired(request.AddressAr),
            AddressHe = BusinessTextNormalizer.NormalizeOptional(request.AddressHe),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var createdBranch = await _repository.AddBranchAsync(branch);
        _logger.LogInfo(
            $"Business branch created for company {company.Id} with branch {createdBranch.Id} by user {userId}.");
        return MapBranch(createdBranch);
    }

    public async Task<BranchResponse> UpdateBranchAsync(
        Guid userId,
        Guid branchId,
        UpdateBranchRequest request)
    {
        _requestValidator.Validate(request);

        var branch = await _repository.GetBranchForUserAsync(userId, branchId)
            ?? throw LogUnauthorizedBranchUpdate(userId, branchId);

        branch.NameAr = BusinessTextNormalizer.NormalizeRequired(request.NameAr);
        branch.NameHe = BusinessTextNormalizer.NormalizeOptional(request.NameHe);
        branch.AddressAr = BusinessTextNormalizer.NormalizeRequired(request.AddressAr);
        branch.AddressHe = BusinessTextNormalizer.NormalizeOptional(request.AddressHe);
        branch.Latitude = request.Latitude;
        branch.Longitude = request.Longitude;
        branch.IsActive = request.IsActive;
        branch.UpdatedAt = DateTime.UtcNow;
        ValidateImplicitServiceAreaCoordinates(branch);

        var updatedBranch = await _repository.UpdateBranchAsync(branch);
        _logger.LogInfo(
            $"Business branch updated for company {updatedBranch.CompanyId} with branch {updatedBranch.Id} by user {userId}.");
        return MapBranch(updatedBranch);
    }

    private async Task<Company> GetAssignedCompanyAsync(Guid userId, string operationName)
    {
        return await _repository.GetForUserAsync(userId)
            ?? throw LogMissingAssignment(userId, operationName);
    }

    private UnauthorizedAccessException LogMissingAssignment(Guid userId, string operationName)
    {
        _logger.LogWarning(
            $"Business company operation '{operationName}' rejected because user {userId} has no active company assignment.");
        return new UnauthorizedAccessException(
            "No active company assignment was found for this business account.");
    }

    private UnauthorizedAccessException LogUnauthorizedBranchUpdate(Guid userId, Guid branchId)
    {
        _logger.LogWarning(
            $"Business branch update rejected because branch {branchId} is not assigned to user {userId}.");
        return new UnauthorizedAccessException(
            "The branch is not assigned to this business account.");
    }

    private static void ValidateImplicitServiceAreaCoordinates(Branch branch)
    {
        if (branch.ServiceArea is null ||
            !branch.ServiceArea.IsActive ||
            branch.ServiceArea.CenterLatitude.HasValue)
        {
            return;
        }

        if (branch.Latitude.HasValue && branch.Longitude.HasValue)
        {
            return;
        }

        throw new ValidationException(
        [
            new ValidationFailure(
                nameof(UpdateBranchRequest.Latitude),
                "Active service areas that use branch coordinates require the branch latitude and longitude to remain set.")
        ]);
    }

    private static CompanyProfileResponse MapCompany(Company company)
    {
        return new CompanyProfileResponse
        {
            Id = company.Id,
            NameAr = company.NameAr,
            NameHe = company.NameHe,
            DescriptionAr = company.DescriptionAr,
            DescriptionHe = company.DescriptionHe,
            ServiceAreaDescriptionAr = company.ServiceAreaDescriptionAr,
            ServiceAreaDescriptionHe = company.ServiceAreaDescriptionHe,
            Phone = company.Phone,
            IsActive = company.IsActive,
            Branches = company.Branches.Select(MapBranch).ToArray()
        };
    }

    private static BranchResponse MapBranch(Branch branch)
    {
        return new BranchResponse
        {
            Id = branch.Id,
            NameAr = branch.NameAr,
            NameHe = branch.NameHe,
            AddressAr = branch.AddressAr,
            AddressHe = branch.AddressHe,
            Latitude = branch.Latitude,
            Longitude = branch.Longitude,
            IsActive = branch.IsActive
        };
    }
}
