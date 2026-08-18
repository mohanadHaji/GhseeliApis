using FluentAssertions;
using Ghseeli.BusinessApi.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:BusinessConnection"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=GhseeliBusiness_Tests;Trusted_Connection=True;TrustServerCertificate=True"
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
    }

    [Fact]
    public void Services_ContainIndependentBusinessDbContext()
    {
        using var scope = _factory.Services.CreateScope();

        var context = scope.ServiceProvider.GetService<BusinessDbContext>();

        context.Should().NotBeNull();
    }
}
