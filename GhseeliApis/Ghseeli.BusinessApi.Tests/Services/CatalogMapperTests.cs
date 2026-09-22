using FluentAssertions;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Services.Catalog;
using Ghseeli.IntegrationContracts.BusinessCatalog;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies focused catalog request/entity and entity/response mapping.
/// </summary>
public class CatalogMapperTests
{
    [Fact]
    public void CreateCategory_NormalizesOptionalHebrewFieldsToNull()
    {
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة غسيلي"
        };
        var request = new CreateServiceCategoryRequest
        {
            NameAr = " تنظيف ",
            NameHe = "   ",
            DescriptionAr = " وصف ",
            DescriptionHe = "   ",
            DisplayOrder = 2,
            IsActive = true
        };
        var timestamp = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

        var category = CatalogMapper.CreateCategory(company, request, timestamp);

        category.CompanyId.Should().Be(company.Id);
        category.NameAr.Should().Be("تنظيف");
        category.NameHe.Should().BeNull();
        category.DescriptionAr.Should().Be("وصف");
        category.DescriptionHe.Should().BeNull();
        category.CreatedAt.Should().Be(timestamp);
    }

    [Fact]
    public void CreateOffering_NormalizesQualifierAndPreservesBadge()
    {
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة غسيلي"
        };
        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "تنظيف"
        };
        var request = new CreateServiceOfferingRequest
        {
            CategoryId = category.Id,
            NameAr = "غسيل كامل",
            QualifierAr = " بدون التعقيم ",
            QualifierHe = "   ",
            BadgeCode = CatalogOfferingBadgeCode.MostRequested,
            BasePrice = 100m,
            DurationMinutes = 45,
            DisplayOrder = 1,
            IsActive = true
        };
        var timestamp = new DateTime(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc);

        var offering = CatalogMapper.CreateOffering(category, null, request, timestamp);

        offering.QualifierAr.Should().Be("بدون التعقيم");
        offering.QualifierHe.Should().BeNull();
        offering.BadgeCode.Should().Be(CatalogOfferingBadgeCode.MostRequested);
    }

    [Fact]
    public void ApplyOfferingUpdate_ClearsQualifierAndBadge()
    {
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة غسيلي"
        };
        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "تنظيف"
        };
        var offering = new ServiceOffering
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Category = category,
            NameAr = "غسيل كامل",
            QualifierAr = "بدون التعقيم",
            QualifierHe = "ללא חיטוי",
            BadgeCode = CatalogOfferingBadgeCode.MostRequested,
            BasePrice = 100m,
            DurationMinutes = 45,
            DisplayOrder = 1,
            IsActive = true
        };
        var request = new UpdateServiceOfferingRequest
        {
            NameAr = offering.NameAr,
            QualifierAr = null,
            QualifierHe = null,
            BadgeCode = null,
            BasePrice = offering.BasePrice,
            DurationMinutes = offering.DurationMinutes,
            DisplayOrder = offering.DisplayOrder,
            IsActive = true
        };

        CatalogMapper.ApplyOfferingUpdate(
            offering,
            null,
            request,
            new DateTime(2026, 9, 22, 8, 30, 0, DateTimeKind.Utc));

        offering.QualifierAr.Should().BeNull();
        offering.QualifierHe.Should().BeNull();
        offering.BadgeCode.Should().BeNull();
    }

    [Fact]
    public void ToResponse_SortsNestedCollectionsAndPreservesNullableHebrewFields()
    {
        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = "شركة غسيلي"
        };
        var category = new ServiceCategory
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "تنظيف",
            NameHe = null
        };
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            NameAr = "الرئيسي",
            NameHe = null,
            AddressAr = "الرياض"
        };
        var offering = new ServiceOffering
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Category = category,
            BranchId = branch.Id,
            Branch = branch,
            NameAr = "غسيل شامل",
            NameHe = null,
            QualifierAr = "بدون التعقيم",
            QualifierHe = null,
            BadgeCode = CatalogOfferingBadgeCode.MostRequested,
            BasePrice = 100m,
            DurationMinutes = 45,
            DisplayOrder = 0,
            IsActive = true
        };
        var laterGroup = new AddonGroup
        {
            Id = Guid.NewGuid(),
            ServiceOfferingId = offering.Id,
            ServiceOffering = offering,
            NameAr = "معطرات",
            DisplayOrder = 2,
            IsActive = true
        };
        laterGroup.Choices.Add(new AddonChoice
        {
            Id = Guid.NewGuid(),
            AddonGroupId = laterGroup.Id,
            AddonGroup = laterGroup,
            NameAr = "فانيلا",
            DisplayOrder = 1,
            IsActive = true
        });
        var firstGroup = new AddonGroup
        {
            Id = Guid.NewGuid(),
            ServiceOfferingId = offering.Id,
            ServiceOffering = offering,
            NameAr = "إضافات",
            DisplayOrder = 1,
            IsActive = true
        };
        firstGroup.Choices.Add(new AddonChoice
        {
            Id = Guid.NewGuid(),
            AddonGroupId = firstGroup.Id,
            AddonGroup = firstGroup,
            NameAr = "تعقيم",
            DisplayOrder = 2,
            IsActive = true
        });
        firstGroup.Choices.Add(new AddonChoice
        {
            Id = Guid.NewGuid(),
            AddonGroupId = firstGroup.Id,
            AddonGroup = firstGroup,
            NameAr = "شمع",
            DisplayOrder = 1,
            IsActive = true
        });
        offering.AddonGroups.Add(laterGroup);
        offering.AddonGroups.Add(firstGroup);

        var response = CatalogMapper.ToResponse(offering);

        response.NameHe.Should().BeNull();
        response.QualifierAr.Should().Be("بدون التعقيم");
        response.QualifierHe.Should().BeNull();
        response.BadgeCode.Should().Be(CatalogOfferingBadgeCode.MostRequested);
        response.CategoryNameHe.Should().BeNull();
        response.BranchNameHe.Should().BeNull();
        response.AddonGroups.Select(group => group.Id).Should().ContainInOrder(firstGroup.Id, laterGroup.Id);
        response.AddonGroups.First().Choices.Select(choice => choice.NameAr).Should().ContainInOrder("شمع", "تعقيم");
    }
}
