using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using System.Net;
using System.Net.Http.Json;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Verifies automatic FluentValidation-powered MVC 400 responses for Business API requests.
/// </summary>
public class ValidationIntegrationTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public ValidationIntegrationTests(CatalogApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RegisterOwner_WhenArabicCompanyNameIsMissing_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/business/auth/register-owner", new
        {
            email = "owner@example.com",
            password = "Password1",
            fullName = "Owner Name",
            companyNameAr = "",
            companyNameHe = (string?)null
        });
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, content);
        content.Should().Contain("errors");
        content.Should().ContainEquivalentOf("CompanyNameAr");
    }

    [Fact]
    public async Task UpdateCompany_WhenHebrewNameExceedsMaxLength_ReturnsBadRequest()
    {
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        var response = await client.PutAsJsonAsync("/api/v1/business/company", new
        {
            nameAr = "شركة غسيلي",
            nameHe = new string('א', 201)
        });
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, content);
        content.Should().Contain("errors");
        content.Should().ContainEquivalentOf("NameHe");
    }

    [Fact]
    public async Task CreateCategory_WhenArabicNameIsMissing_ReturnsValidationProblemInsteadOfCatalogDomainPayload()
    {
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        var response = await client.PostAsJsonAsync("/api/v1/business/catalog/categories", new
        {
            nameAr = " ",
            displayOrder = 0,
            isActive = true
        });
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, content);
        content.Should().Contain("errors");
        content.Should().ContainEquivalentOf("NameAr");
        content.Should().NotContainEquivalentOf("\"message\"");
    }
}
