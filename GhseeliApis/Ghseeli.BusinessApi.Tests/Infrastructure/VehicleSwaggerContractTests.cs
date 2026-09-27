using System.Text.Json;
using FluentAssertions;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Verifies Business OpenAPI vehicle snapshot contracts.
/// </summary>
public sealed class VehicleSwaggerContractTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public VehicleSwaggerContractTests(CatalogApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Swagger_DescribesExactStringVehicleEnumAndHttpsImage()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        var vehicleType = schemas.GetProperty("VehicleType");
        vehicleType.GetProperty("type").GetString().Should().Be("string");
        vehicleType.GetProperty("enum").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal(
                "Sedan",
                "Motorcycle",
                "Suv5Seater",
                "Suv7Seater",
                "Van7Seater");

        var image = schemas.GetProperty("ReservationVehicleSnapshot")
            .GetProperty("properties")
            .GetProperty("imageUrl");
        image.GetProperty("nullable").GetBoolean().Should().BeTrue();
        image.GetProperty("maxLength").GetInt32().Should().Be(500);
        image.GetProperty("pattern").GetString().Should().StartWith("^https:");
        image.GetProperty("description").GetString()
            .Should().Contain("without embedded credentials");
    }
}
