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
    [Fact]
    public void ReservationContracts_RoundTripCrossSystemReferencesAndMoney()
    {
        var options = BusinessCatalogContract.CreateJsonSerializerOptions();
        var request = new CreateReservationRequest
        {
            BookingReference = Guid.NewGuid(),
            OrderGuid = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            ExpectedCatalogVersion = 12,
            RequestedSlotStartUtc = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            Currency = "ILS",
            ExpectedItemSubtotal = 123.45m,
            ExpectedTotalDurationMinutes = 60,
            Customer = new ReservationCustomerSnapshot { Name = "Customer" },
            Vehicle = new ReservationVehicleSnapshot { VehicleType = "Sedan" },
            Location = new ReservationLocationSnapshot { AddressLine = "Street 1" },
            CancellationPolicyAcknowledged = true,
            Items =
            [
                new CreateReservationItemRequest
                {
                    OfferingId = Guid.NewGuid(),
                    ExpectedBaseSubtotal = 100m,
                    ExpectedAddonSubtotal = 23.45m,
                    ExpectedItemSubtotal = 123.45m,
                    ExpectedDurationMinutes = 60
                }
            ]
        };

        var json = JsonSerializer.Serialize(request, options);
        var roundTrip = JsonSerializer.Deserialize<CreateReservationRequest>(json, options);

        roundTrip.Should().NotBeNull();
        roundTrip!.BookingReference.Should().Be(request.BookingReference);
        roundTrip.OrderGuid.Should().Be(request.OrderGuid);
        roundTrip.ExpectedItemSubtotal.Should().Be(123.45m);
        roundTrip.Items.Should().ContainSingle();
        roundTrip.Items.Single().OfferingId.Should().Be(request.Items.Single().OfferingId);
    }

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

    [Fact]
    public void CatalogSnapshotResponse_RoundTripsBranchAvailabilityRules()
    {
        var response = new CatalogSnapshotResponse
        {
            GeneratedAtUtc = new DateTime(2026, 8, 24, 9, 30, 0, DateTimeKind.Utc),
            Company = new CatalogSnapshotCompany
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                NameAr = "شركة"
            },
            Branches =
            [
                new CatalogSnapshotBranch
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    NameAr = "فرع",
                    AddressAr = "عنوان",
                    Availability = new CatalogSnapshotBranchAvailability
                    {
                        IsActive = true,
                        TimeZoneId = "UTC",
                        MinimumLeadMinutes = 30,
                        BookingHorizonDays = 20,
                        RecurringSchedules =
                        [
                            new CatalogSnapshotRecurringSchedule
                            {
                                DayOfWeek = DayOfWeek.Monday,
                                StartLocalTime = TimeSpan.FromHours(8),
                                EndLocalTime = TimeSpan.FromHours(18),
                                SlotDurationMinutes = 30,
                                Capacity = 4
                            }
                        ],
                        AvailabilityOverrides =
                        [
                            new CatalogSnapshotAvailabilityOverride
                            {
                                OverrideDate = new DateOnly(2026, 8, 25),
                                IsClosed = true
                            }
                        ]
                    }
                }
            ]
        };

        var payload = JsonSerializer.Serialize(response, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<CatalogSnapshotResponse>(payload, JsonOptions);

        roundTripped.Should().NotBeNull();
        roundTripped!.Branches.Single().Availability!.TimeZoneId.Should().Be("UTC");
        roundTripped.Branches.Single().Availability!.RecurringSchedules.Single().SlotDurationMinutes.Should().Be(30);
        roundTripped.Branches.Single().Availability!.AvailabilityOverrides.Single().OverrideDate
            .Should()
            .Be(new DateOnly(2026, 8, 25));
    }
}
