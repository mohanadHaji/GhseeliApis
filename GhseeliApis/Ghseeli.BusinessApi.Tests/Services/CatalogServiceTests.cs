using FluentAssertions;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Catalog;
using Ghseeli.BusinessApi.Services.Validation.Catalog;
using Ghseeli.BusinessApi.Validators.Catalog;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Defines catalog ownership and localized offering behavior for the Business API.
/// </summary>
public class CatalogServiceTests
{
    private readonly Mock<ICompanyRepository> _companyRepository = new();
    private readonly Mock<ICatalogRepository> _catalogRepository = new();
    private readonly CatalogService _service;

    public CatalogServiceTests()
    {
        _service = new CatalogService(
            _companyRepository.Object,
            _catalogRepository.Object,
            new CatalogRequestValidator(
                new CreateServiceCategoryRequestValidator(),
                new UpdateServiceCategoryRequestValidator(),
                new CreateServiceOfferingRequestValidator(),
                new UpdateServiceOfferingRequestValidator(),
                new CreateAddonGroupRequestValidator(new CreateAddonChoiceRequestValidator()),
                new UpdateAddonGroupRequestValidator(),
                new CreateAddonChoiceRequestValidator(),
                new UpdateAddonChoiceRequestValidator()),
            new CatalogRuleValidator());
    }

    [Fact]
    public async Task CreateCategoryAsync_WhenOwnerHasAssignedCompany_CreatesLocalizedCategory()
    {
        var userId = Guid.NewGuid();
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة",
            NameHe = "חברה"
        };

        _companyRepository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(company);
        _catalogRepository.Setup(repository =>
                repository.AddCategoryAsync(It.IsAny<ServiceCategory>()))
            .ReturnsAsync((ServiceCategory category) => category);

        var result = await _service.CreateCategoryAsync(userId, false, new CreateServiceCategoryRequest
        {
            NameAr = " تنظيف ",
            DescriptionAr = "وصف",
            DescriptionHe = "תיאור",
            DisplayOrder = 3,
            IsActive = true
        });

        result.CompanyId.Should().Be(company.Id);
        result.NameAr.Should().Be("تنظيف");
        result.NameHe.Should().BeNull();
        result.DisplayOrder.Should().Be(3);
        result.IsActive.Should().BeTrue();
        _catalogRepository.Verify(repository => repository.AddCategoryAsync(
            It.Is<ServiceCategory>(category =>
                category.CompanyId == company.Id &&
                category.NameAr == "تنظيف" &&
                category.NameHe == null)),
            Times.Once);
    }

    [Fact]
    public async Task CreateOfferingAsync_WhenHebrewNameIsOmitted_CreatesOffering()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Company = new Company
            {
                Id = companyId,
                NameAr = "شركة"
            },
            NameAr = "خدمات"
        };

        _companyRepository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(new Company
            {
                Id = companyId,
                NameAr = "شركة"
            });
        _catalogRepository.Setup(repository => repository.GetCategoryByIdAsync(category.Id))
            .ReturnsAsync(category);
        _catalogRepository.Setup(repository => repository.AddOfferingAsync(It.IsAny<ServiceOffering>()))
            .ReturnsAsync((ServiceOffering offering) => offering);

        var result = await _service.CreateOfferingAsync(userId, false, new CreateServiceOfferingRequest
        {
            CategoryId = category.Id,
            NameAr = "غسيل خارجي",
            BasePrice = 50m,
            DurationMinutes = 30,
            DisplayOrder = 1,
            IsActive = true
        });

        result.NameAr.Should().Be("غسيل خارجي");
        result.NameHe.Should().BeNull();
        _catalogRepository.Verify(repository => repository.AddOfferingAsync(
            It.Is<ServiceOffering>(offering =>
                offering.CategoryId == category.Id &&
                offering.NameAr == "غسيل خارجي" &&
                offering.NameHe == null)),
            Times.Once);
    }

    [Fact]
    public async Task CreateOfferingAsync_WhenBranchBelongsToDifferentCompany_HidesExistence()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Company = new Company
            {
                Id = companyId,
                NameAr = "شركة",
                NameHe = "חברה"
            },
            NameAr = "خدمات",
            NameHe = "שירותים"
        };
        var branchId = Guid.NewGuid();

        _companyRepository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(new Company
            {
                Id = companyId,
                NameAr = "شركة",
                NameHe = "חברה"
            });
        _catalogRepository.Setup(repository => repository.GetCategoryByIdAsync(category.Id))
            .ReturnsAsync(category);
        _companyRepository.Setup(repository => repository.GetBranchByIdAsync(branchId))
            .ReturnsAsync(new Branch
            {
                Id = branchId,
                CompanyId = otherCompanyId,
                NameAr = "فرع",
                NameHe = "סניף",
                AddressAr = "عنوان",
                AddressHe = "כתובת"
            });

        var action = () => _service.CreateOfferingAsync(userId, false, new CreateServiceOfferingRequest
        {
            CategoryId = category.Id,
            BranchId = branchId,
            NameAr = "غسيل خارجي",
            NameHe = "שטיפה חיצונית",
            BasePrice = 50m,
            DurationMinutes = 30,
            DisplayOrder = 1,
            IsActive = true
        });

        await action.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("The branch was not found.");
        _catalogRepository.Verify(repository => repository.AddOfferingAsync(
            It.IsAny<ServiceOffering>()), Times.Never);
    }

    [Fact]
    public async Task GetCategoriesAsync_WhenAdminOmitsCompanyId_RejectsRequest()
    {
        var userId = Guid.NewGuid();

        var action = () => _service.GetCategoriesAsync(userId, true, null);

        await action.Should().ThrowAsync<CatalogValidationException>()
            .WithMessage("*companyId*");
    }

    [Fact]
    public async Task CreateCategoryAsync_WhenArabicNameIsMissing_ThrowsCatalogValidationException()
    {
        var userId = Guid.NewGuid();

        var action = () => _service.CreateCategoryAsync(userId, false, new CreateServiceCategoryRequest
        {
            NameAr = "   ",
            DisplayOrder = 0,
            IsActive = true
        });

        await action.Should().ThrowAsync<CatalogValidationException>()
            .Where(exception =>
                exception.Errors.ContainsKey("nameAr") &&
                exception.Errors["nameAr"].Any(message => message.Contains("required", StringComparison.OrdinalIgnoreCase)));
        _companyRepository.Verify(repository => repository.GetForUserAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CreateAddonGroupAsync_WhenFixedIncludedChoiceDoesNotHaveSingleActiveDefault_RejectsRequest()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var offering = CreateOwnedOffering(companyId);

        _companyRepository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(new Company
            {
                Id = companyId,
                NameAr = "شركة",
                NameHe = "חברה"
            });
        _catalogRepository.Setup(repository => repository.GetOfferingByIdAsync(offering.Id))
            .ReturnsAsync(offering);

        var action = () => _service.CreateAddonGroupAsync(userId, false, offering.Id, new CreateAddonGroupRequest
        {
            NameAr = "يتضمن",
            NameHe = "כלול",
            SelectionType = AddonSelectionType.FixedIncludedChoice,
            IsRequired = true,
            MinimumSelections = 1,
            MaximumSelections = 1,
            DisplayOrder = 0,
            IsActive = true,
            Choices =
            [
                new CreateAddonChoiceRequest
                {
                    NameAr = "افتراضي",
                    NameHe = "ברירת מחדל",
                    PriceAdjustment = 0m,
                    DurationAdjustmentMinutes = 0,
                    DefaultQuantity = 0,
                    DisplayOrder = 0,
                    IsActive = true
                }
            ]
        });

        await action.Should().ThrowAsync<CatalogValidationException>()
            .WithMessage("*default*");
        _catalogRepository.Verify(repository => repository.AddAddonGroupAsync(
            It.IsAny<AddonGroup>()), Times.Never);
    }

    [Fact]
    public async Task CreateAddonGroupAsync_WhenMultipleChoiceRulesAreValid_CreatesChoices()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var offering = CreateOwnedOffering(companyId);

        _companyRepository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(new Company
            {
                Id = companyId,
                NameAr = "شركة",
                NameHe = "חברה"
            });
        _catalogRepository.Setup(repository => repository.GetOfferingByIdAsync(offering.Id))
            .ReturnsAsync(offering);
        _catalogRepository.Setup(repository => repository.AddAddonGroupAsync(It.IsAny<AddonGroup>()))
            .ReturnsAsync((AddonGroup group) => group);

        var result = await _service.CreateAddonGroupAsync(userId, false, offering.Id, new CreateAddonGroupRequest
        {
            NameAr = "الإضافات",
            NameHe = "תוספות",
            SelectionType = AddonSelectionType.MultipleChoice,
            IsRequired = true,
            MinimumSelections = 1,
            MaximumSelections = 2,
            DisplayOrder = 2,
            IsActive = true,
            Choices =
            [
                new CreateAddonChoiceRequest
                {
                    NameAr = "شمع",
                    NameHe = "ווקס",
                    PriceAdjustment = 15m,
                    DurationAdjustmentMinutes = 10,
                    DefaultQuantity = 1,
                    DisplayOrder = 1,
                    IsActive = true
                },
                new CreateAddonChoiceRequest
                {
                    NameAr = "تعقيم",
                    NameHe = "חיטוי",
                    PriceAdjustment = 20m,
                    DurationAdjustmentMinutes = 5,
                    DefaultQuantity = 0,
                    DisplayOrder = 2,
                    IsActive = true
                }
            ]
        });

        result.SelectionType.Should().Be(AddonSelectionType.MultipleChoice);
        result.Choices.Should().HaveCount(2);
        result.Choices.Should().ContainSingle(choice =>
            choice.NameAr == "شمع" &&
            choice.DefaultQuantity == 1 &&
            choice.PriceAdjustment == 15m);
        _catalogRepository.Verify(repository => repository.AddAddonGroupAsync(
            It.Is<AddonGroup>(group =>
                group.ServiceOfferingId == offering.Id &&
                group.Choices.Count == 2 &&
                group.Choices.Any(choice => choice.NameAr == "شمع" && choice.DefaultQuantity == 1))),
            Times.Once);
    }

    [Fact]
    public async Task CreateAddonGroupAsync_WhenHebrewNamesAreOmitted_CreatesGroupAndChoice()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var offering = CreateOwnedOffering(companyId);

        _companyRepository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(new Company
            {
                Id = companyId,
                NameAr = "شركة"
            });
        _catalogRepository.Setup(repository => repository.GetOfferingByIdAsync(offering.Id))
            .ReturnsAsync(offering);
        _catalogRepository.Setup(repository => repository.AddAddonGroupAsync(It.IsAny<AddonGroup>()))
            .ReturnsAsync((AddonGroup group) => group);

        var result = await _service.CreateAddonGroupAsync(userId, false, offering.Id, new CreateAddonGroupRequest
        {
            NameAr = "الإضافات",
            SelectionType = AddonSelectionType.MultipleChoice,
            IsRequired = false,
            MinimumSelections = 0,
            MaximumSelections = 2,
            DisplayOrder = 1,
            IsActive = true,
            Choices =
            [
                new CreateAddonChoiceRequest
                {
                    NameAr = "تعقيم",
                    PriceAdjustment = 12m,
                    DurationAdjustmentMinutes = 5,
                    DefaultQuantity = 0,
                    DisplayOrder = 0,
                    IsActive = true
                }
            ]
        });

        result.NameAr.Should().Be("الإضافات");
        result.NameHe.Should().BeNull();
        result.Choices.Should().ContainSingle();
        result.Choices.Single().NameHe.Should().BeNull();
    }

    [Fact]
    public async Task CreateAddonChoiceAsync_WhenInactiveChoiceIsDefault_RejectsRequest()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var group = CreateOwnedAddonGroup(companyId, AddonSelectionType.MultipleChoice);

        _companyRepository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(new Company
            {
                Id = companyId,
                NameAr = "شركة",
                NameHe = "חברה"
            });
        _catalogRepository.Setup(repository => repository.GetAddonGroupByIdAsync(group.Id))
            .ReturnsAsync(group);

        var action = () => _service.CreateAddonChoiceAsync(userId, false, group.Id, new CreateAddonChoiceRequest
        {
            NameAr = "بوليش",
            NameHe = "פוליש",
            PriceAdjustment = 12m,
            DurationAdjustmentMinutes = 8,
            DefaultQuantity = 1,
            DisplayOrder = 0,
            IsActive = false
        });

        await action.Should().ThrowAsync<CatalogValidationException>()
            .WithMessage("*Inactive choices*");
        _catalogRepository.Verify(repository => repository.AddAddonChoiceAsync(
            It.IsAny<AddonChoice>()), Times.Never);
    }

    [Fact]
    public async Task CreateAddonChoiceAsync_WhenHebrewNameIsOmitted_CreatesChoice()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var group = CreateOwnedAddonGroup(companyId, AddonSelectionType.MultipleChoice);

        _companyRepository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(new Company
            {
                Id = companyId,
                NameAr = "شركة"
            });
        _catalogRepository.Setup(repository => repository.GetAddonGroupByIdAsync(group.Id))
            .ReturnsAsync(group);
        _catalogRepository.Setup(repository => repository.AddAddonChoiceAsync(It.IsAny<AddonChoice>()))
            .ReturnsAsync((AddonChoice choice) => choice);

        var result = await _service.CreateAddonChoiceAsync(userId, false, group.Id, new CreateAddonChoiceRequest
        {
            NameAr = "بوليش",
            PriceAdjustment = 12m,
            DurationAdjustmentMinutes = 8,
            DefaultQuantity = 0,
            DisplayOrder = 0,
            IsActive = true
        });

        result.NameAr.Should().Be("بوليش");
        result.NameHe.Should().BeNull();
        _catalogRepository.Verify(repository => repository.AddAddonChoiceAsync(
            It.Is<AddonChoice>(choice => choice.NameAr == "بوليش" && choice.NameHe == null)),
            Times.Once);
    }

    [Fact]
    public async Task GetOfferingAsync_ReturnsNestedAddonGroupsAndChoices()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var offering = CreateOwnedOffering(companyId);
        var group = CreateOwnedAddonGroup(companyId, AddonSelectionType.SingleChoice, offering);
        group.Choices.Add(new AddonChoice
        {
            Id = Guid.NewGuid(),
            AddonGroupId = group.Id,
            AddonGroup = group,
            NameAr = "قياسي",
            NameHe = "רגיל",
            PriceAdjustment = 0m,
            DurationAdjustmentMinutes = 0,
            DefaultQuantity = 1,
            DisplayOrder = 0,
            IsActive = true
        });
        offering.AddonGroups.Add(group);

        _companyRepository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(new Company
            {
                Id = companyId,
                NameAr = "شركة",
                NameHe = "חברה"
            });
        _catalogRepository.Setup(repository => repository.GetOfferingByIdAsync(offering.Id))
            .ReturnsAsync(offering);

        var result = await _service.GetOfferingAsync(userId, false, offering.Id);

        result.AddonGroups.Should().ContainSingle();
        result.AddonGroups.Single().Choices.Should().ContainSingle(choice =>
            choice.NameAr == "قياسي" && choice.DefaultQuantity == 1);
    }

    [Fact]
    public async Task CreateAddonGroupAsync_WhenFixedIncludedChoiceChangesPriceOrDuration_RejectsRequest()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var offering = CreateOwnedOffering(companyId);

        _companyRepository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(new Company
            {
                Id = companyId,
                NameAr = "شركة",
                NameHe = "חברה"
            });
        _catalogRepository.Setup(repository => repository.GetOfferingByIdAsync(offering.Id))
            .ReturnsAsync(offering);

        var action = () => _service.CreateAddonGroupAsync(userId, false, offering.Id, new CreateAddonGroupRequest
        {
            NameAr = "يتضمن",
            NameHe = "כלול",
            SelectionType = AddonSelectionType.FixedIncludedChoice,
            IsRequired = true,
            MinimumSelections = 1,
            MaximumSelections = 1,
            DisplayOrder = 0,
            IsActive = true,
            Choices =
            [
                new CreateAddonChoiceRequest
                {
                    NameAr = "قياسي",
                    NameHe = "רגיל",
                    PriceAdjustment = 5m,
                    DurationAdjustmentMinutes = 1,
                    DefaultQuantity = 1,
                    DisplayOrder = 0,
                    IsActive = true
                }
            ]
        });

        await action.Should().ThrowAsync<CatalogValidationException>()
            .WithMessage("*price or duration*");
    }

    [Fact]
    public async Task DeleteAddonChoiceAsync_WhenRemovingLastActiveChoiceFromActiveGroup_RejectsRequest()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var group = CreateOwnedAddonGroup(companyId, AddonSelectionType.SingleChoice);
        var choice = new AddonChoice
        {
            Id = Guid.NewGuid(),
            AddonGroupId = group.Id,
            AddonGroup = group,
            NameAr = "قياسي",
            NameHe = "רגיל",
            PriceAdjustment = 0m,
            DurationAdjustmentMinutes = 0,
            DefaultQuantity = 1,
            DisplayOrder = 0,
            IsActive = true
        };
        group.MaximumSelections = 1;
        group.Choices.Add(choice);

        _companyRepository.Setup(repository => repository.GetForUserAsync(userId))
            .ReturnsAsync(new Company
            {
                Id = companyId,
                NameAr = "شركة",
                NameHe = "חברה"
            });
        _catalogRepository.Setup(repository => repository.GetAddonChoiceByIdAsync(choice.Id))
            .ReturnsAsync(choice);

        var action = () => _service.DeleteAddonChoiceAsync(userId, false, choice.Id);

        await action.Should().ThrowAsync<CatalogValidationException>()
            .WithMessage("*at least one active choice*");
        _catalogRepository.Verify(repository => repository.DeleteAddonChoiceAsync(
            It.IsAny<AddonChoice>()), Times.Never);
    }

    private static ServiceOffering CreateOwnedOffering(Guid companyId)
    {
        var company = new Company
        {
            Id = companyId,
            NameAr = "شركة",
            NameHe = "חברה"
        };
        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Company = company,
            NameAr = "تنظيف",
            NameHe = "ניקוי"
        };

        return new ServiceOffering
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Category = category,
            NameAr = "غسيل",
            NameHe = "שטיפה",
            BasePrice = 50m,
            DurationMinutes = 30,
            DisplayOrder = 0,
            IsActive = true
        };
    }

    private static AddonGroup CreateOwnedAddonGroup(
        Guid companyId,
        AddonSelectionType selectionType,
        ServiceOffering? offering = null)
    {
        offering ??= CreateOwnedOffering(companyId);

        return new AddonGroup
        {
            Id = Guid.NewGuid(),
            ServiceOfferingId = offering.Id,
            ServiceOffering = offering,
            NameAr = "إضافات",
            NameHe = "תוספות",
            SelectionType = selectionType,
            IsRequired = false,
            MinimumSelections = 0,
            MaximumSelections = 3,
            DisplayOrder = 0,
            IsActive = true
        };
    }
}
