using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Contracts;

/// <summary>
/// Verifies shared Business catalog contract serialization settings and versioned field names.
/// </summary>
public class BusinessCatalogContractSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        BusinessCatalogContract.CreateJsonSerializerOptions();

    [Fact]
    public void CatalogSnapshotResponse_SerializesWithCamelCaseContractVersionAndNullableFields()
    {
        var response = new CatalogSnapshotResponse
        {
            GeneratedAtUtc = new DateTime(2026, 8, 24, 9, 30, 0, DateTimeKind.Utc),
            Company = new CatalogSnapshotCompany
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                NameAr = "شركة",
                NameHe = null
            }
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));

        document.RootElement.TryGetProperty("contractVersion", out var contractVersion).Should().BeTrue();
        contractVersion.GetString().Should().Be(BusinessCatalogContract.Version);
        document.RootElement.TryGetProperty("generatedAtUtc", out var generatedAtUtc).Should().BeTrue();
        generatedAtUtc.GetDateTime().Should().Be(response.GeneratedAtUtc);
        document.RootElement.GetProperty("company").TryGetProperty("nameHe", out var nameHe).Should().BeTrue();
        nameHe.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ValidateAppointmentContracts_RoundTripVersionDecimalsAndUtcOffset()
    {
        var request = new ValidateAppointmentRequest
        {
            BranchId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            OfferingId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            RequestedSlotStartUtc = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.FromHours(3)),
            Currency = "ILS"
        };
        var response = new ValidateAppointmentResponse
        {
            ContractVersion = BusinessCatalogContract.Version,
            Valid = true,
            CatalogVersion = 8,
            Currency = "ILS",
            BranchId = request.BranchId,
            OfferingId = request.OfferingId,
            BaseSubtotal = 50.01m,
            AddonSubtotal = 20.02m,
            TotalPrice = 70.03m
        };

        using var requestDocument = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));
        using var responseDocument = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));

        requestDocument.RootElement.GetProperty("contractVersion").GetString()
            .Should()
            .Be(BusinessCatalogContract.Version);
        requestDocument.RootElement.GetProperty("requestedSlotStartUtc").GetDateTimeOffset()
            .Should()
            .Be(request.RequestedSlotStartUtc);
        responseDocument.RootElement.GetProperty("baseSubtotal").GetDecimal().Should().Be(50.01m);
        responseDocument.RootElement.GetProperty("addonSubtotal").GetDecimal().Should().Be(20.02m);
        responseDocument.RootElement.GetProperty("totalPrice").GetDecimal().Should().Be(70.03m);
    }
}
