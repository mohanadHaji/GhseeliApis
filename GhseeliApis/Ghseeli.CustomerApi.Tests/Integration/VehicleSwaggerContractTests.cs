using System.Text.Json;
using FluentAssertions;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Verifies the saved-vehicle and vehicle-snapshot OpenAPI contracts.
/// </summary>
public sealed class VehicleSwaggerContractTests
    : IClassFixture<Step15SwaggerCustomerFactory>
{
    private static readonly string[] VehicleTypes =
        ["Sedan", "Motorcycle", "Suv5Seater", "Suv7Seater", "Van7Seater"];
    private readonly Step15SwaggerCustomerFactory _factory;

    public VehicleSwaggerContractTests(Step15SwaggerCustomerFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Swagger_DescribesVehicleEnumImageOperationsStatusesAndAuth()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");

        var vehicleType = schemas.GetProperty("VehicleType");
        vehicleType.GetProperty("type").GetString().Should().Be("string");
        vehicleType.TryGetProperty("format", out _).Should().BeFalse();
        vehicleType.GetProperty("enum").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal(VehicleTypes);

        foreach (var schemaName in new[]
                 {
                     "CreateVehicleRequest",
                     "UpdateVehicleRequest",
                     "VehicleResponse",
                     "CheckoutDraftVehicleRequest",
                     "CheckoutDraftVehicleResponse",
                     "ConfirmedBookingVehicleResponse"
                 })
        {
            var image = schemas.GetProperty(schemaName)
                .GetProperty("properties")
                .GetProperty("imageUrl");
            image.GetProperty("nullable").GetBoolean().Should().BeTrue(schemaName);
            image.GetProperty("maxLength").GetInt32().Should().Be(500, schemaName);
            image.GetProperty("pattern").GetString().Should().StartWith("^https:", schemaName);
            image.GetProperty("description").GetString()
                .Should().Contain("without embedded credentials", schemaName);
        }

        AssertOperation(root, "/api/Vehicles/my-vehicles", "get", ["200", "401", "500"]);
        AssertOperation(root, "/api/Vehicles/{id}", "get", ["200", "401", "404", "500"]);
        AssertOperation(root, "/api/Vehicles", "post", ["201", "400", "401", "500"]);
        AssertOperation(root, "/api/Vehicles/{id}", "put", ["200", "400", "401", "404", "500"]);
        AssertOperation(root, "/api/Vehicles/{id}", "delete", ["204", "400", "401", "404", "500"]);
    }

    private static void AssertOperation(
        JsonElement root,
        string path,
        string method,
        string[] statuses)
    {
        var operation = root.GetProperty("paths").GetProperty(path).GetProperty(method);
        operation.GetProperty("responses").EnumerateObject()
            .Select(value => value.Name)
            .Should().Contain(statuses);
        operation.GetProperty("security").EnumerateArray()
            .Any(value => value.TryGetProperty("CustomerBearer", out _))
            .Should().BeTrue();
    }
}
