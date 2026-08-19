using FluentAssertions;
using Ghseeli.Common.Logging;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Ghseeli.BusinessApi.Tests.Repositories;

/// <summary>
/// Verifies catalog persistence and ownership-scoped loading for categories and offerings.
/// </summary>
public class CatalogRepositoryTests
{
    [Fact]
    public async Task GetCategoriesForCompanyAsync_ReturnsOnlyRequestedCompanyCategoriesInDisplayOrder()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new BusinessDbContext(options);
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة",
            NameHe = "חברה"
        };
        var otherCompany = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة أخرى",
            NameHe = "חברה אחרת"
        };
        var second = new ServiceCategory
        {
            CompanyId = company.Id,
            Company = company,
            NameAr = "خدمات ثانية",
            NameHe = "שירות שני",
            DisplayOrder = 2
        };
        var first = new ServiceCategory
        {
            CompanyId = company.Id,
            Company = company,
            NameAr = "خدمات أولى",
            NameHe = "שירות ראשון",
            DisplayOrder = 1
        };
        var outsider = new ServiceCategory
        {
            CompanyId = otherCompany.Id,
            Company = otherCompany,
            NameAr = "خارجية",
            NameHe = "חיצוני",
            DisplayOrder = 0
        };

        context.AddRange(company, otherCompany, second, first, outsider);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var logger = new Mock<IAppLogger>();
        var repository = new CatalogRepository(context, logger.Object);

        var result = await repository.GetCategoriesForCompanyAsync(company.Id);

        result.Select(category => category.Id).Should().ContainInOrder(first.Id, second.Id);
        result.Should().OnlyContain(category => category.CompanyId == company.Id);
    }

    [Fact]
    public async Task GetOfferingByIdAsync_ReturnsOfferingWithCategoryAndBranchMetadata()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new BusinessDbContext(options);
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة",
            NameHe = "חברה"
        };
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "الرئيسي",
            NameHe = "ראשי",
            AddressAr = "العنوان",
            AddressHe = "כתובת"
        };
        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "تنظيف",
            NameHe = "ניקוי"
        };
        var offering = new ServiceOffering
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Category = category,
            BranchId = branch.Id,
            Branch = branch,
            NameAr = "غسيل شامل",
            NameHe = "שטיפה מלאה",
            BasePrice = 120m,
            DurationMinutes = 45,
            DisplayOrder = 4,
            IsActive = true
        };

        context.AddRange(company, branch, category, offering);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var logger = new Mock<IAppLogger>();
        var repository = new CatalogRepository(context, logger.Object);

        var result = await repository.GetOfferingByIdAsync(offering.Id);

        result.Should().NotBeNull();
        result!.Category.NameAr.Should().Be(category.NameAr);
        result.Branch.Should().NotBeNull();
        result.Branch!.NameHe.Should().Be(branch.NameHe);
    }

    [Fact]
    public async Task GetOfferingByIdAsync_ReturnsNestedAddonGroupsAndChoices()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new BusinessDbContext(options);
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة",
            NameHe = "חברה"
        };
        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "تنظيف",
            NameHe = "ניקוי"
        };
        var offering = new ServiceOffering
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Category = category,
            NameAr = "غسيل داخلي",
            NameHe = "שטיפה פנימית",
            BasePrice = 90m,
            DurationMinutes = 35,
            DisplayOrder = 2,
            IsActive = true
        };
        var addonGroup = new AddonGroup
        {
            Id = Guid.NewGuid(),
            ServiceOfferingId = offering.Id,
            ServiceOffering = offering,
            NameAr = "عطور",
            NameHe = "בשמים",
            SelectionType = AddonSelectionType.SingleChoice,
            IsRequired = false,
            MinimumSelections = 0,
            MaximumSelections = 1,
            DisplayOrder = 1,
            IsActive = true
        };
        var addonChoice = new AddonChoice
        {
            Id = Guid.NewGuid(),
            AddonGroupId = addonGroup.Id,
            AddonGroup = addonGroup,
            NameAr = "فانيلا",
            NameHe = "וניל",
            PriceAdjustment = 5m,
            DurationAdjustmentMinutes = 0,
            DefaultQuantity = 1,
            DisplayOrder = 0,
            IsActive = true
        };
        addonGroup.Choices.Add(addonChoice);
        offering.AddonGroups.Add(addonGroup);

        context.AddRange(company, category, offering, addonGroup, addonChoice);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var logger = new Mock<IAppLogger>();
        var repository = new CatalogRepository(context, logger.Object);

        var result = await repository.GetOfferingByIdAsync(offering.Id);

        result.Should().NotBeNull();
        result!.AddonGroups.Should().ContainSingle();
        result.AddonGroups.Single().Choices.Should().ContainSingle(choice =>
            choice.NameAr == addonChoice.NameAr &&
            choice.DefaultQuantity == 1);
    }

    [Fact]
    public async Task AddCategoryAsync_LogsInfoAfterPersistingCategory()
    {
        var options = new DbContextOptionsBuilder<BusinessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new BusinessDbContext(options);
        var logger = new Mock<IAppLogger>();
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة",
            NameHe = "חברה"
        };
        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "غسيل",
            NameHe = "שטיפה",
            DisplayOrder = 1,
            IsActive = true
        };
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var repository = new CatalogRepository(context, logger.Object);

        var result = await repository.AddCategoryAsync(category);

        result.Id.Should().Be(category.Id);
        logger.Verify(log => log.LogInfo(
            It.Is<string>(message =>
                message.Contains("category created", StringComparison.OrdinalIgnoreCase) &&
                message.Contains(category.Id.ToString(), StringComparison.OrdinalIgnoreCase) &&
                message.Contains(company.Id.ToString(), StringComparison.OrdinalIgnoreCase))),
            Times.Once);
    }
}
