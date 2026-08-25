using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;

namespace Ghseeli.BusinessApi.Services;

public class CatalogPublicationService : ICatalogPublicationService
{
    private readonly ICompanyRepository _companyRepository;

    public CatalogPublicationService(ICompanyRepository companyRepository)
    {
        _companyRepository = companyRepository;
    }

    public async Task<CatalogSnapshotResponse> GetSnapshotAsync(Guid companyId)
    {
        var company = EnsureCompanyIsActive(
            await _companyRepository.GetPublicationByIdAsync(companyId));
        var activeBranches = company.Branches
            .Where(branch => branch.IsActive)
            .OrderBy(branch => branch.NameAr)
            .ToArray();

        var activeCategories = company.Categories
            .Where(category =>
                category.IsActive &&
                category.BusinessVerticalId == Models.BusinessVerticalDefaults.CarWashId)
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.NameAr)
            .ToArray();
        var publishedServiceAreas = activeBranches
            .Select(CreateSnapshotServiceArea)
            .Where(serviceArea => serviceArea is not null)
            .Cast<CatalogSnapshotServiceArea>()
            .ToArray();

        return new CatalogSnapshotResponse
        {
            ContractVersion = BusinessCatalogContract.Version,
            CatalogVersion = company.CatalogVersion,
            GeneratedAtUtc = DateTime.UtcNow,
            Company = new CatalogSnapshotCompany
            {
                Id = company.Id,
                NameAr = company.NameAr,
                NameHe = company.NameHe,
                DescriptionAr = company.DescriptionAr,
                DescriptionHe = company.DescriptionHe,
                Phone = company.Phone
            },
            Branches = activeBranches
                .Select(branch => new CatalogSnapshotBranch
                {
                    Id = branch.Id,
                    NameAr = branch.NameAr,
                    NameHe = branch.NameHe,
                    AddressAr = branch.AddressAr,
                    AddressHe = branch.AddressHe,
                    Latitude = branch.Latitude,
                    Longitude = branch.Longitude,
                    Availability = CreateSnapshotAvailability(branch)
                })
                .ToArray(),
            ServiceAreas = publishedServiceAreas,
            Categories = activeCategories
                .Select(category => new CatalogSnapshotCategory
                {
                    Id = category.Id,
                    NameAr = category.NameAr,
                    NameHe = category.NameHe,
                    DescriptionAr = category.DescriptionAr,
                    DescriptionHe = category.DescriptionHe,
                    DisplayOrder = category.DisplayOrder,
                    Offerings = category.Offerings
                        .Where(offering => offering.IsActive)
                        .Where(offering => offering.BranchId is null ||
                            activeBranches.Any(branch => branch.Id == offering.BranchId))
                        .OrderBy(offering => offering.DisplayOrder)
                        .ThenBy(offering => offering.NameAr)
                        .Select(offering => new CatalogSnapshotOffering
                        {
                            Id = offering.Id,
                            BranchId = offering.BranchId,
                            NameAr = offering.NameAr,
                            NameHe = offering.NameHe,
                            DescriptionAr = offering.DescriptionAr,
                            DescriptionHe = offering.DescriptionHe,
                            BasePrice = RoundMoney(offering.BasePrice),
                            DurationMinutes = offering.DurationMinutes,
                            ImageUrl = offering.ImageUrl,
                            ReferenceCode = offering.ReferenceCode,
                            DisplayOrder = offering.DisplayOrder,
                            AddonGroups = offering.AddonGroups
                                .Where(group => group.IsActive)
                                .OrderBy(group => group.DisplayOrder)
                                .ThenBy(group => group.NameAr)
                                .Select(group => new CatalogSnapshotAddonGroup
                                {
                                    Id = group.Id,
                                    NameAr = group.NameAr,
                                    NameHe = group.NameHe,
                                    DescriptionAr = group.DescriptionAr,
                                    DescriptionHe = group.DescriptionHe,
                                    SelectionType = group.SelectionType.ToString(),
                                    IsRequired = group.IsRequired,
                                    MinimumSelections = group.MinimumSelections,
                                    MaximumSelections = group.MaximumSelections,
                                    DisplayOrder = group.DisplayOrder,
                                    Choices = group.Choices
                                        .Where(choice => choice.IsActive)
                                        .OrderBy(choice => choice.DisplayOrder)
                                        .ThenBy(choice => choice.NameAr)
                                        .Select(choice => new CatalogSnapshotAddonChoice
                                        {
                                            Id = choice.Id,
                                            NameAr = choice.NameAr,
                                            NameHe = choice.NameHe,
                                            DescriptionAr = choice.DescriptionAr,
                                            DescriptionHe = choice.DescriptionHe,
                                            PriceAdjustment = RoundMoney(choice.PriceAdjustment),
                                            DurationAdjustmentMinutes = choice.DurationAdjustmentMinutes,
                                            DefaultQuantity = choice.DefaultQuantity,
                                            DisplayOrder = choice.DisplayOrder
                                        })
                                        .ToArray()
                                })
                                .ToArray()
                        })
                        .ToArray()
                })
                .ToArray()
        };
    }

    private static CatalogSnapshotServiceArea? CreateSnapshotServiceArea(Models.Branch branch)
    {
        if (branch.ServiceArea is null || !branch.ServiceArea.IsActive)
        {
            return null;
        }

        var centerLatitude = branch.ServiceArea.CenterLatitude ?? branch.Latitude;
        var centerLongitude = branch.ServiceArea.CenterLongitude ?? branch.Longitude;
        if (!centerLatitude.HasValue || !centerLongitude.HasValue)
        {
            return null;
        }

        return new CatalogSnapshotServiceArea
        {
            BranchId = branch.Id,
            IsActive = true,
            UsesBranchCoordinates = !branch.ServiceArea.CenterLatitude.HasValue,
            CenterLatitude = centerLatitude,
            CenterLongitude = centerLongitude,
            RadiusKm = branch.ServiceArea.RadiusKm
        };
    }

    private static CatalogSnapshotBranchAvailability? CreateSnapshotAvailability(Models.Branch branch)
    {
        if (branch.AvailabilitySettings is null)
        {
            return null;
        }

        return new CatalogSnapshotBranchAvailability
        {
            IsActive = branch.AvailabilitySettings.IsActive,
            TimeZoneId = branch.AvailabilitySettings.TimeZoneId,
            MinimumLeadMinutes = branch.AvailabilitySettings.MinimumLeadMinutes,
            BookingHorizonDays = branch.AvailabilitySettings.BookingHorizonDays,
            RecurringSchedules = branch.RecurringSchedules
                .Where(schedule => schedule.IsActive)
                .OrderBy(schedule => schedule.DayOfWeek)
                .ThenBy(schedule => schedule.StartLocalTime)
                .Select(schedule => new CatalogSnapshotRecurringSchedule
                {
                    DayOfWeek = schedule.DayOfWeek,
                    StartLocalTime = schedule.StartLocalTime,
                    EndLocalTime = schedule.EndLocalTime,
                    SlotDurationMinutes = schedule.SlotDurationMinutes,
                    Capacity = schedule.Capacity
                })
                .ToArray(),
            AvailabilityOverrides = branch.AvailabilityOverrides
                .Where(overrideItem => overrideItem.IsActive)
                .OrderBy(overrideItem => overrideItem.OverrideDate)
                .Select(overrideItem => new CatalogSnapshotAvailabilityOverride
                {
                    OverrideDate = overrideItem.OverrideDate,
                    IsClosed = overrideItem.IsClosed,
                    StartLocalTime = overrideItem.StartLocalTime,
                    EndLocalTime = overrideItem.EndLocalTime,
                    SlotDurationMinutes = overrideItem.SlotDurationMinutes,
                    Capacity = overrideItem.Capacity
                })
                .ToArray()
        };
    }

    private static Models.Company EnsureCompanyIsActive(Models.Company? company)
    {
        if (company is null || !company.IsActive)
        {
            throw new KeyNotFoundException("The company was not found or is inactive.");
        }

        return company;
    }

    private static decimal RoundMoney(decimal value)
    {
        return BusinessMoney.RoundToCurrency(value);
    }
}
