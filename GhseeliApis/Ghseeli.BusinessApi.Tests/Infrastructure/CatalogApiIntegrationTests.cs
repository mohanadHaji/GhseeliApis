using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
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
}

public class CatalogApiFactory : WebApplicationFactory<Program>
{
    private const string JwtSecret = "CatalogIntegrationTestsSecretKey_Minimum32Characters";
    private const string JwtIssuer = "Ghseeli.BusinessApi.CatalogTests";
    private const string JwtAudience = "Ghseeli.BusinessApi.CatalogClients";
    private readonly string _databaseName = $"CatalogApiTests-{Guid.NewGuid()}";

    public Guid OwnerUserId { get; } = Guid.NewGuid();
    public Guid EmployeeUserId { get; } = Guid.NewGuid();
    public Guid AdminUserId { get; } = Guid.NewGuid();
    public Guid CompanyId { get; } = Guid.NewGuid();
    public Guid BranchId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:BusinessConnection", "CatalogApiTests");
        builder.UseSetting("BusinessJwtSettings:SecretKey", JwtSecret);
        builder.UseSetting("BusinessJwtSettings:Issuer", JwtIssuer);
        builder.UseSetting("BusinessJwtSettings:Audience", JwtAudience);
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
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId, roles));
        return client;
    }

    private string CreateToken(Guid userId, IEnumerable<string> roles)
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
            expires: DateTime.UtcNow.AddMinutes(30),
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
        var branch = new Branch
        {
            Id = BranchId,
            CompanyId = CompanyId,
            Company = company,
            NameAr = "الرئيسي",
            NameHe = "ראשי",
            AddressAr = "العنوان",
            AddressHe = "כתובת",
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
            branch,
            owner,
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
