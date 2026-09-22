using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.InternalServices;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Exercises the Business catalog HTTP surface, authorization, and Swagger documentation.
/// </summary>
public class CatalogApiIntegrationTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public CatalogApiIntegrationTests(CatalogApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateCategory_AsOwner_CreatesCategoryForAssignedCompany()
    {
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        var response = await client.PostAsJsonAsync("/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = "غسيل",
                NameHe = "שטיפה",
                DescriptionAr = "وصف",
                DescriptionHe = "תיאור",
                DisplayOrder = 2,
                IsActive = true
            });

        var responseContent = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, responseContent);
        var body = await response.Content.ReadFromJsonAsync<ServiceCategoryResponse>();
        body.Should().NotBeNull();
        body!.CompanyId.Should().Be(_factory.CompanyId);
        body.NameAr.Should().Be("غسيل");
    }

    [Fact]
    public async Task CreateCategory_AsAdmin_UsesExplicitCompanyId()
    {
        var client = _factory.CreateAuthenticatedClient(_factory.AdminUserId, BusinessRoles.Admin);

        var response = await client.PostAsJsonAsync("/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                CompanyId = _factory.CompanyId,
                NameAr = "تلميع",
                NameHe = "ליטוש",
                DisplayOrder = 3,
                IsActive = true
            });

        var responseContent = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, responseContent);
        var body = await response.Content.ReadFromJsonAsync<ServiceCategoryResponse>();
        body.Should().NotBeNull();
        body!.CompanyId.Should().Be(_factory.CompanyId);
    }

    [Fact]
    public async Task CreateCategory_AsEmployee_ReturnsForbidden()
    {
        var client = _factory.CreateAuthenticatedClient(_factory.EmployeeUserId, BusinessRoles.Employee);

        var response = await client.PostAsJsonAsync("/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = "محظور",
                NameHe = "אסור",
                DisplayOrder = 0,
                IsActive = true
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCategory_AsOwnerFromDifferentCompany_ReturnsNotFound()
    {
        var ownerClient = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var otherOwnerClient = _factory.CreateAuthenticatedClient(_factory.OtherOwnerUserId, BusinessRoles.Owner);

        var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = "مخفي",
                DisplayOrder = 0,
                IsActive = true
            });
        var created = await createResponse.Content.ReadFromJsonAsync<ServiceCategoryResponse>();

        var foreignResponse = await otherOwnerClient.GetAsync(
            $"/api/v1/business/catalog/categories/{created!.Id}");
        var missingResponse = await otherOwnerClient.GetAsync(
            $"/api/v1/business/catalog/categories/{Guid.NewGuid()}");

        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreignResponse.Content.ReadAsStringAsync())
            .Should()
            .Be(await missingResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UpdateCategory_AsOwnerFromDifferentCompany_ReturnsNotFound()
    {
        var ownerClient = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var otherOwnerClient = _factory.CreateAuthenticatedClient(_factory.OtherOwnerUserId, BusinessRoles.Owner);

        var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = "مخفي للتحديث",
                DisplayOrder = 0,
                IsActive = true
            });
        var created = await createResponse.Content.ReadFromJsonAsync<ServiceCategoryResponse>();

        var foreignResponse = await otherOwnerClient.PutAsJsonAsync(
            $"/api/v1/business/catalog/categories/{created!.Id}",
            new UpdateServiceCategoryRequest
            {
                NameAr = "لن تصل",
                DisplayOrder = 1,
                IsActive = true
            });
        var missingResponse = await otherOwnerClient.PutAsJsonAsync(
            $"/api/v1/business/catalog/categories/{Guid.NewGuid()}",
            new UpdateServiceCategoryRequest
            {
                NameAr = "لن تصل",
                DisplayOrder = 1,
                IsActive = true
            });

        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreignResponse.Content.ReadAsStringAsync())
            .Should()
            .Be(await missingResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateAddonGroup_WhenSelectionRulesAreInvalid_ReturnsBadRequest()
    {
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        var categoryResponse = await client.PostAsJsonAsync("/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = "داخلية",
                NameHe = "פנימי",
                DisplayOrder = 1,
                IsActive = true
            });
        (await categoryResponse.Content.ReadAsStringAsync()).Should().NotContain("System.");
        categoryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = await categoryResponse.Content.ReadFromJsonAsync<ServiceCategoryResponse>();

        var offeringResponse = await client.PostAsJsonAsync("/api/v1/business/catalog/offerings",
            new CreateServiceOfferingRequest
            {
                CategoryId = category!.Id,
                NameAr = "غسيل داخلي",
                NameHe = "שטיפה פנימית",
                BasePrice = 80m,
                DurationMinutes = 25,
                DisplayOrder = 0,
                IsActive = true
            });
        (await offeringResponse.Content.ReadAsStringAsync()).Should().NotContain("System.");
        offeringResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var offering = await offeringResponse.Content.ReadFromJsonAsync<ServiceOfferingResponse>();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/business/catalog/offerings/{offering!.Id}/addon-groups",
            new CreateAddonGroupRequest
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
                        PriceAdjustment = 0m,
                        DurationAdjustmentMinutes = 0,
                        DefaultQuantity = 0,
                        DisplayOrder = 0,
                        IsActive = true
                    }
                ]
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("default");
    }

    [Fact]
    public async Task OfferingMetadata_CreateUpdateListAndDetail_RoundTripsStringBadgeAndClears()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var categoryResponse = await client.PostAsJsonAsync(
            "/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = "خدمات",
                DisplayOrder = 0,
                IsActive = true
            });
        var category = await categoryResponse.Content.ReadFromJsonAsync<ServiceCategoryResponse>();

        var createPayload = $$"""
            {
              "categoryId": "{{category!.Id}}",
              "nameAr": "غسيل كامل",
              "qualifierAr": "بدون التعقيم",
              "qualifierHe": "ללא חיטוי",
              "badgeCode": "MostRequested",
              "basePrice": 100,
              "durationMinutes": 45,
              "displayOrder": 0,
              "isActive": true
            }
            """;
        var createResponse = await client.PostAsync(
            "/api/v1/business/catalog/offerings",
            new StringContent(createPayload, Encoding.UTF8, "application/json"));
        var createJson = await createResponse.Content.ReadAsStringAsync();

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, createJson);
        createJson.Should().Contain("\"badgeCode\":\"MostRequested\"");
        var created = JsonSerializer.Deserialize<ServiceOfferingResponse>(
            createJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
        created!.QualifierAr.Should().Be("بدون التعقيم");

        var listResponse = await client.GetAsync("/api/v1/business/catalog/offerings");
        var listJson = await listResponse.Content.ReadAsStringAsync();
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, listJson);
        listJson.Should().Contain("\"qualifierAr\":\"بدون التعقيم\"");
        listJson.Should().Contain("\"badgeCode\":\"MostRequested\"");

        var detailResponse = await client.GetAsync(
            $"/api/v1/business/catalog/offerings/{created.Id}?language=he");
        var detailJson = await detailResponse.Content.ReadAsStringAsync();
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK, detailJson);
        detailJson.Should().Contain("\"qualifierHe\":\"ללא חיטוי\"");
        detailJson.Should().NotContain("\"qualifierAr\"");
        detailJson.Should().Contain("\"badgeCode\":\"MostRequested\"");

        var versionBeforeUpdate = _factory.ReadState(context =>
            context.Companies.Single(company => company.Id == _factory.CompanyId).CatalogVersion);
        var replacementResponse = await client.PutAsJsonAsync(
            $"/api/v1/business/catalog/offerings/{created.Id}",
            new UpdateServiceOfferingRequest
            {
                NameAr = created.NameAr,
                QualifierAr = "  بدون التلميع  ",
                QualifierHe = "  ללא פוליש  ",
                BadgeCode = CatalogOfferingBadgeCode.MostRequested,
                BasePrice = created.BasePrice,
                DurationMinutes = created.DurationMinutes,
                IsActive = true
            });
        var replacement = await replacementResponse.Content.ReadFromJsonAsync<ServiceOfferingResponse>();

        replacementResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replacement!.QualifierAr.Should().Be("بدون التلميع");
        replacement.QualifierHe.Should().Be("ללא פוליש");
        _factory.ReadState(context =>
                context.Companies.Single(company => company.Id == _factory.CompanyId).CatalogVersion)
            .Should().Be(versionBeforeUpdate + 1);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/business/catalog/offerings/{created.Id}",
            new UpdateServiceOfferingRequest
            {
                NameAr = created.NameAr,
                QualifierAr = " ",
                QualifierHe = null,
                BadgeCode = null,
                BasePrice = created.BasePrice,
                DurationMinutes = created.DurationMinutes,
                IsActive = true
            });
        var updateJson = await updateResponse.Content.ReadAsStringAsync();

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK, updateJson);
        using var updateDocument = JsonDocument.Parse(updateJson);
        updateDocument.RootElement.GetProperty("qualifierAr").ValueKind.Should().Be(JsonValueKind.Null);
        updateDocument.RootElement.GetProperty("qualifierHe").ValueKind.Should().Be(JsonValueKind.Null);
        updateDocument.RootElement.GetProperty("badgeCode").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("\"Popular\"")]
    [InlineData("0")]
    public async Task OfferingMetadata_WhenBadgeIsUnknownOrInteger_ReturnsBadRequestWithoutMutation(
        string badgeJson)
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var categoryResponse = await client.PostAsJsonAsync(
            "/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = "خدمات",
                DisplayOrder = 0,
                IsActive = true
            });
        var category = await categoryResponse.Content.ReadFromJsonAsync<ServiceCategoryResponse>();
        var versionBeforeRequest = _factory.ReadState(context =>
            context.Companies.Single(company => company.Id == _factory.CompanyId).CatalogVersion);
        var payload = $$"""
            {
              "categoryId": "{{category!.Id}}",
              "nameAr": "غسيل كامل",
              "badgeCode": {{badgeJson}},
              "basePrice": 100,
              "durationMinutes": 45,
              "displayOrder": 0,
              "isActive": true
            }
            """;

        using var response = await client.PostAsync(
            "/api/v1/business/catalog/offerings",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        var offerings = await client.GetFromJsonAsync<ServiceOfferingListResponse[]>(
            "/api/v1/business/catalog/offerings");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        offerings.Should().BeEmpty();
        _factory.ReadState(context =>
                context.Companies.Single(company => company.Id == _factory.CompanyId).CatalogVersion)
            .Should().Be(versionBeforeRequest);
    }

    [Fact]
    public async Task OfferingMetadata_WhenQualifierExceedsMaximum_ReturnsBadRequestWithoutVersionChange()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var categoryResponse = await client.PostAsJsonAsync(
            "/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = "خدمات",
                DisplayOrder = 0,
                IsActive = true
            });
        var category = await categoryResponse.Content.ReadFromJsonAsync<ServiceCategoryResponse>();
        var versionBeforeRequest = _factory.ReadState(context =>
            context.Companies.Single(company => company.Id == _factory.CompanyId).CatalogVersion);

        var response = await client.PostAsJsonAsync(
            "/api/v1/business/catalog/offerings",
            new CreateServiceOfferingRequest
            {
                CategoryId = category!.Id,
                NameAr = "غسيل كامل",
                QualifierAr = new string('ع', 201),
                BasePrice = 100,
                DurationMinutes = 45,
                IsActive = true
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.ReadState(context => context.ServiceOfferings.Count()).Should().Be(0);
        _factory.ReadState(context =>
                context.Companies.Single(company => company.Id == _factory.CompanyId).CatalogVersion)
            .Should().Be(versionBeforeRequest);
    }

    [Fact]
    public async Task OfferingMetadata_AdminEmployeeAndForeignOwner_PreserveAuthorizationAndVersions()
    {
        _factory.ResetState();
        var adminClient = _factory.CreateAuthenticatedClient(_factory.AdminUserId, BusinessRoles.Admin);
        var ownerClient = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var employeeClient = _factory.CreateAuthenticatedClient(_factory.EmployeeUserId, BusinessRoles.Employee);
        var foreignOwnerClient = _factory.CreateAuthenticatedClient(
            _factory.OtherOwnerUserId,
            BusinessRoles.Owner);

        var categoryResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                CompanyId = _factory.CompanyId,
                NameAr = "خدمات الإدارة",
                DisplayOrder = 0,
                IsActive = true
            });
        var category = await categoryResponse.Content.ReadFromJsonAsync<ServiceCategoryResponse>();

        var adminPayload = $$"""
            {
              "companyId": "{{_factory.CompanyId}}",
              "categoryId": "{{category!.Id}}",
              "nameAr": "خدمة مميزة",
              "qualifierAr": "بدون التعقيم",
              "badgeCode": "MostRequested",
              "basePrice": 50,
              "durationMinutes": 30,
              "displayOrder": 0,
              "isActive": true
            }
            """;
        var adminCreate = await adminClient.PostAsync(
            "/api/v1/business/catalog/offerings",
            new StringContent(adminPayload, Encoding.UTF8, "application/json"));
        var offering = await adminCreate.Content.ReadFromJsonAsync<ServiceOfferingResponse>();

        adminCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        offering!.QualifierAr.Should().Be("بدون التعقيم");
        offering.BadgeCode.Should().Be(CatalogOfferingBadgeCode.MostRequested);

        var versionBeforeRejectedRequests = _factory.ReadState(context =>
            context.Companies.Single(company => company.Id == _factory.CompanyId).CatalogVersion);
        var employeeCreate = await employeeClient.PostAsJsonAsync(
            "/api/v1/business/catalog/offerings",
            new CreateServiceOfferingRequest
            {
                CategoryId = category.Id,
                NameAr = "محظورة",
                QualifierAr = "لن تحفظ",
                BadgeCode = CatalogOfferingBadgeCode.MostRequested,
                BasePrice = 10,
                DurationMinutes = 10,
                IsActive = true
            });
        var employeeUpdate = await employeeClient.PutAsJsonAsync(
            $"/api/v1/business/catalog/offerings/{offering.Id}",
            new UpdateServiceOfferingRequest
            {
                NameAr = offering.NameAr,
                QualifierAr = "لن تحفظ",
                BadgeCode = null,
                BasePrice = offering.BasePrice,
                DurationMinutes = offering.DurationMinutes,
                IsActive = true
            });
        var foreignUpdate = await foreignOwnerClient.PutAsJsonAsync(
            $"/api/v1/business/catalog/offerings/{offering.Id}",
            new UpdateServiceOfferingRequest
            {
                NameAr = offering.NameAr,
                QualifierAr = "لن تحفظ",
                BadgeCode = null,
                BasePrice = offering.BasePrice,
                DurationMinutes = offering.DurationMinutes,
                IsActive = true
            });

        employeeCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        employeeUpdate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreignUpdate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.ReadState(context =>
                context.Companies.Single(company => company.Id == _factory.CompanyId).CatalogVersion)
            .Should().Be(versionBeforeRejectedRequests);
        _factory.ReadState(context => context.ServiceOfferings.Count())
            .Should().Be(1);
        var persisted = _factory.ReadState(context =>
            context.ServiceOfferings.Single(value => value.Id == offering.Id));
        persisted.QualifierAr.Should().Be("بدون التعقيم");
        persisted.BadgeCode.Should().Be(CatalogOfferingBadgeCode.MostRequested);
    }

    [Fact]
    public async Task OfferingMetadata_WithHebrewOnlyQualifier_LocalizesWithoutLeakingOtherLanguage()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var categoryResponse = await client.PostAsJsonAsync(
            "/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = "خدمات",
                DisplayOrder = 0,
                IsActive = true
            });
        var category = await categoryResponse.Content.ReadFromJsonAsync<ServiceCategoryResponse>();
        var payload = $$"""
            {
              "categoryId": "{{category!.Id}}",
              "nameAr": "غسيل كامل",
              "qualifierAr": null,
              "qualifierHe": "ללא חיטוי",
              "basePrice": 100,
              "durationMinutes": 45,
              "displayOrder": 0,
              "isActive": true
            }
            """;
        var createResponse = await client.PostAsync(
            "/api/v1/business/catalog/offerings",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        var createdJson = await createResponse.Content.ReadAsStringAsync();
        using var createdDocument = JsonDocument.Parse(createdJson);
        var offeringId = createdDocument.RootElement.GetProperty("id").GetGuid();

        var arabicResponse = await client.GetAsync(
            $"/api/v1/business/catalog/offerings/{offeringId}?language=ar");
        var arabicJson = await arabicResponse.Content.ReadAsStringAsync();
        var hebrewResponse = await client.GetAsync(
            $"/api/v1/business/catalog/offerings/{offeringId}?language=he");
        var hebrewJson = await hebrewResponse.Content.ReadAsStringAsync();

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, createdJson);
        arabicResponse.StatusCode.Should().Be(HttpStatusCode.OK, arabicJson);
        arabicJson.Should().Contain("\"qualifierAr\":null");
        arabicJson.Should().NotContain("\"qualifierHe\"");
        hebrewResponse.StatusCode.Should().Be(HttpStatusCode.OK, hebrewJson);
        hebrewJson.Should().Contain("\"qualifierHe\":\"ללא חיטוי\"");
        hebrewJson.Should().NotContain("\"qualifierAr\"");
    }

    [Fact]
    public async Task SwaggerDocument_ListsCatalogRoutesAndSelectionTypeNames()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("/api/v1/business/catalog/categories");
        content.Should().Contain("/api/v1/business/catalog/offerings/{offeringId}/addon-groups");
        content.Should().Contain("FixedIncludedChoice");
        content.Should().Contain("SegmentedSingleButtonChoice");
        content.Should().Contain("minimumSelections");
        content.Should().Contain("defaultQuantity");
    }

    [Fact]
    public async Task SwaggerDocument_RepresentativeSchemasMatchRuntimeRequiredAndNullability()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(content);
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        var categoryRequest = schemas.GetProperty("CreateServiceCategoryRequest");
        categoryRequest.GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Should()
            .Contain("nameAr");
        IsNullable(categoryRequest.GetProperty("properties").GetProperty("nameAr"))
            .Should()
            .BeFalse();

        var validateRequest = schemas.GetProperty("ValidateAppointmentRequest");
        validateRequest.GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Should()
            .Contain(["branchId", "offeringId", "requestedSlotStartUtc", "currency"]);
        IsNullable(validateRequest.GetProperty("properties").GetProperty("currency"))
            .Should()
            .BeFalse();
        IsNullable(validateRequest.GetProperty("properties").GetProperty("selectedAddons"))
            .Should()
            .BeTrue();
    }

    private static bool IsNullable(JsonElement propertySchema)
    {
        return propertySchema.TryGetProperty("nullable", out var nullableElement) &&
            nullableElement.GetBoolean();
    }
}

public class CatalogApiFactory : WebApplicationFactory<Program>
{
    private const string JwtSecret = "CatalogIntegrationTestsSecretKey_Minimum32Characters";
    private const string JwtIssuer = "Ghseeli.BusinessApi.CatalogTests";
    private const string JwtAudience = "Ghseeli.BusinessApi.CatalogClients";
    public const string InternalServiceId = "customer-api-tests";
    public const string InternalServiceActiveSecret = "Step6TestSecret_Active_Minimum32Characters";
    public const string InternalServiceNextSecret = "Step6TestSecret_Next_Minimum32Characters__";
    public const string SnapshotOnlyServiceId = "snapshot-reader-tests";
    public const string SnapshotOnlySecret = "Step6SnapshotOnlySecret_Minimum32Chars";
    private readonly string _databaseName = $"CatalogApiTests-{Guid.NewGuid()}";

    public Guid OwnerUserId { get; } = Guid.NewGuid();
    public Guid EmployeeUserId { get; } = Guid.NewGuid();
    public Guid OtherOwnerUserId { get; } = Guid.NewGuid();
    public Guid AdminUserId { get; } = Guid.NewGuid();
    public Guid CompanyId { get; } = Guid.NewGuid();
    public Guid OtherCompanyId { get; } = Guid.NewGuid();
    public Guid BranchId { get; } = Guid.NewGuid();
    public Guid OtherBranchId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:BusinessConnection", "CatalogApiTests");
        builder.UseSetting("BusinessJwtSettings:SecretKey", JwtSecret);
        builder.UseSetting("BusinessJwtSettings:Issuer", JwtIssuer);
        builder.UseSetting("BusinessJwtSettings:Audience", JwtAudience);
        builder.UseSetting("InternalServiceAuthentication:RequireHttps", "true");
        builder.UseSetting("InternalServiceAuthentication:AllowInsecureHttpInDevelopment", "false");
        builder.UseSetting("InternalServiceAuthentication:AllowedClockSkewSeconds", "120");
        builder.UseSetting("InternalServiceAuthentication:NonceLifetimeSeconds", "300");
        builder.UseSetting("InternalServiceAuthentication:IdempotencyLifetimeSeconds", "300");
        builder.UseSetting("InternalServiceAuthentication:Services:0:ServiceId", InternalServiceId);
        builder.UseSetting("InternalServiceAuthentication:Services:0:ActiveSecret", InternalServiceActiveSecret);
        builder.UseSetting("InternalServiceAuthentication:Services:0:NextSecret", InternalServiceNextSecret);
        builder.UseSetting("InternalServiceAuthentication:Services:0:AllowedOperations:0", InternalServiceOperationNames.CatalogSnapshot);
        builder.UseSetting("InternalServiceAuthentication:Services:0:AllowedOperations:1", InternalServiceOperationNames.AppointmentValidate);
        builder.UseSetting("InternalServiceAuthentication:Services:0:AllowedOperations:2", InternalServiceOperationNames.AppointmentAvailableSlots);
        builder.UseSetting("InternalServiceAuthentication:Services:1:ServiceId", SnapshotOnlyServiceId);
        builder.UseSetting("InternalServiceAuthentication:Services:1:ActiveSecret", SnapshotOnlySecret);
        builder.UseSetting("InternalServiceAuthentication:Services:1:AllowedOperations:0", InternalServiceOperationNames.CatalogSnapshot);
        builder.UseSetting("CustomerBookingStatusClient:BaseUrl", "https://customer.example");
        builder.UseSetting("CustomerBookingStatusClient:ServiceId", "business-api-tests");
        builder.UseSetting(
            "CustomerBookingStatusClient:ActiveSecret",
            "CatalogCallbackSecret_Minimum32Characters");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<BusinessDbContext>));
            services.RemoveAll<BusinessDbContext>();
            services.AddDbContext<BusinessDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            using var scope = services.BuildServiceProvider().CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
            Seed(context);
        });
    }

    public HttpClient CreateAuthenticatedClient(Guid userId, params string[] roles)
    {
        var client = CreateSecureClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId, roles));
        return client;
    }

    public HttpClient CreateExpiredAuthenticatedClient(Guid userId, params string[] roles)
    {
        var client = CreateSecureClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(
                userId,
                roles,
                DateTime.UtcNow.AddMinutes(-1)));
        return client;
    }

    public HttpClient CreateSecureClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    public void ResetState()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
        Seed(context);
    }

    public void MutateState(Action<BusinessDbContext> mutation)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        mutation(context);
        context.SaveChanges();
    }

    public TResult ReadState<TResult>(Func<BusinessDbContext, TResult> read)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        return read(context);
    }

    private string CreateToken(
        Guid userId,
        IEnumerable<string> roles,
        DateTime? expires = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, "catalog@test.com"),
            new(ClaimTypes.Name, "Catalog Test User")
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private void Seed(BusinessDbContext context)
    {
        var company = new Company
        {
            Id = CompanyId,
            NameAr = "شركة الاختبار",
            NameHe = "חברת בדיקה",
            IsActive = true
        };
        var otherCompany = new Company
        {
            Id = OtherCompanyId,
            NameAr = "شركة أخرى",
            NameHe = "חברה אחרת",
            IsActive = true
        };
        var branch = new Branch
        {
            Id = BranchId,
            CompanyId = CompanyId,
            Company = company,
            NameAr = "الرئيسي",
            NameHe = "ראשי",
            AddressAr = "العنوان",
            AddressHe = "כתובת",
            Latitude = 24.7136,
            Longitude = 46.6753,
            IsActive = true
        };
        var otherBranch = new Branch
        {
            Id = OtherBranchId,
            CompanyId = OtherCompanyId,
            Company = otherCompany,
            NameAr = "الآخر",
            NameHe = "אחר",
            AddressAr = "عنوان آخر",
            AddressHe = "כתובת אחרת",
            Latitude = 24.7136,
            Longitude = 46.6753,
            IsActive = true
        };
        var owner = new BusinessUser
        {
            Id = OwnerUserId,
            UserName = "owner@catalog.test",
            Email = "owner@catalog.test",
            FullName = "Catalog Owner",
            IsActive = true
        };
        var otherOwner = new BusinessUser
        {
            Id = OtherOwnerUserId,
            UserName = "other-owner@catalog.test",
            Email = "other-owner@catalog.test",
            FullName = "Other Catalog Owner",
            IsActive = true
        };
        var employee = new BusinessUser
        {
            Id = EmployeeUserId,
            UserName = "employee@catalog.test",
            Email = "employee@catalog.test",
            FullName = "Catalog Employee",
            IsActive = true
        };

        context.AddRange(
            company,
            otherCompany,
            branch,
            otherBranch,
            owner,
            otherOwner,
            employee,
            new BusinessUserAssignment
            {
                UserId = OwnerUserId,
                CompanyId = CompanyId,
                Role = BusinessMembershipRole.Owner,
                IsActive = true,
                Company = company,
                User = owner
            },
            new BusinessUserAssignment
            {
                UserId = OtherOwnerUserId,
                CompanyId = OtherCompanyId,
                Role = BusinessMembershipRole.Owner,
                IsActive = true,
                Company = otherCompany,
                User = otherOwner
            },
            new BusinessUserAssignment
            {
                UserId = EmployeeUserId,
                CompanyId = CompanyId,
                BranchId = BranchId,
                Role = BusinessMembershipRole.Employee,
                IsActive = true,
                Company = company,
                Branch = branch,
                User = employee
            });

        context.SaveChanges();
    }
}
