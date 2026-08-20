using FluentAssertions;
using Ghseeli.BusinessApi.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Verifies that the Business API is independently hosted and exposes its own infrastructure.
/// </summary>
public class BusinessApiStartupTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BusinessApiStartupTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting(
                "ConnectionStrings:BusinessConnection",
                "Server=(localdb)\\MSSQLLocalDB;Database=GhseeliBusiness_Tests;Trusted_Connection=True;TrustServerCertificate=True");
            builder.UseSetting(
                "BusinessJwtSettings:SecretKey",
                "BusinessStartupTestSecretKey_Minimum32Characters");
            builder.UseSetting(
                "BusinessJwtSettings:Issuer",
                "Ghseeli.BusinessApi.Tests");
            builder.UseSetting(
                "BusinessJwtSettings:Audience",
                "Ghseeli.BusinessClients.Tests");
            builder.UseSetting(
                "InternalServiceAuthentication:Services:0:ServiceId",
                CatalogApiFactory.InternalServiceId);
            builder.UseSetting(
                "InternalServiceAuthentication:Services:0:ActiveSecret",
                CatalogApiFactory.InternalServiceActiveSecret);
            builder.UseSetting(
                "InternalServiceAuthentication:Services:0:AllowedOperations:0",
                Ghseeli.IntegrationContracts.InternalHttp.InternalServiceOperationNames.CatalogSnapshot);
            builder.UseSetting(
                "InternalServiceAuthentication:Services:0:AllowedOperations:1",
                Ghseeli.IntegrationContracts.InternalHttp.InternalServiceOperationNames.AppointmentValidate);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:BusinessConnection"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=GhseeliBusiness_Tests;Trusted_Connection=True;TrustServerCertificate=True",
                    ["BusinessJwtSettings:SecretKey"] =
                        "BusinessStartupTestSecretKey_Minimum32Characters",
                    ["BusinessJwtSettings:Issuer"] = "Ghseeli.BusinessApi.Tests",
                    ["BusinessJwtSettings:Audience"] = "Ghseeli.BusinessClients.Tests",
                    ["InternalServiceAuthentication:Services:0:ServiceId"] = CatalogApiFactory.InternalServiceId,
                    ["InternalServiceAuthentication:Services:0:ActiveSecret"] = CatalogApiFactory.InternalServiceActiveSecret,
                    ["InternalServiceAuthentication:Services:0:AllowedOperations:0"] =
                        Ghseeli.IntegrationContracts.InternalHttp.InternalServiceOperationNames.CatalogSnapshot,
                    ["InternalServiceAuthentication:Services:0:AllowedOperations:1"] =
                        Ghseeli.IntegrationContracts.InternalHttp.InternalServiceOperationNames.AppointmentValidate
                });
            });
        });
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsStringAsync()).Should().Contain("Healthy");
    }

    [Fact]
    public async Task SwaggerDocument_IsAvailableForBusinessApi()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        content.Should().Contain("\"title\": \"Ghseeli Business API\"");
        content.Should().Contain("\"Bearer\"");
    }

    [Fact]
    public void Services_ContainIndependentBusinessDbContext()
    {
        using var scope = _factory.Services.CreateScope();

        var context = scope.ServiceProvider.GetService<BusinessDbContext>();

        context.Should().NotBeNull();
    }

    [Fact]
    public async Task CompanyEndpoint_RejectsTokenIssuedForCustomerApi()
    {
        var customerToken = new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(
                issuer: "Ghseeli.CustomerApi",
                audience: "Ghseeli.CustomerClients",
                claims: [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                        "CustomerTestSecretKey_Minimum32Characters")),
                    SecurityAlgorithms.HmacSha256)));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", customerToken);

        var response = await client.GetAsync("/api/v1/business/company");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Startup_WhenInternalServiceIdsAreDuplicated_ThrowsOptionsValidationException()
    {
        using var invalidFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "InternalServiceAuthentication:Services:1:ServiceId",
                CatalogApiFactory.InternalServiceId);
            builder.UseSetting(
                "InternalServiceAuthentication:Services:1:ActiveSecret",
                "BusinessStartupDuplicateServiceSecret_Minimum32Chars");
            builder.UseSetting(
                "InternalServiceAuthentication:Services:1:AllowedOperations:0",
                Ghseeli.IntegrationContracts.InternalHttp.InternalServiceOperationNames.CatalogSnapshot);
        });

        var action = () => invalidFactory.CreateClient();

        action.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Startup_WhenNextSecretIsInvalid_ThrowsOptionsValidationException()
    {
        using var invalidFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting(
                "InternalServiceAuthentication:Services:0:NextSecret",
                "too-short"));

        var action = () => invalidFactory.CreateClient();

        action.Should().Throw<OptionsValidationException>();
    }
}
