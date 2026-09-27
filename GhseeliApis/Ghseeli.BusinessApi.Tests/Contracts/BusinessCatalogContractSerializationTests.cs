using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using Ghseeli.IntegrationContracts.Vehicles;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Contracts;

/// <summary>
/// Verifies shared Business catalog contract serialization settings and versioned field names.
/// </summary>
public class BusinessCatalogContractSerializationTests
{
    [Theory]
    [InlineData(VehicleType.Sedan, "Sedan")]
    [InlineData(VehicleType.Motorcycle, "Motorcycle")]
    [InlineData(VehicleType.Suv5Seater, "Suv5Seater")]
    [InlineData(VehicleType.Suv7Seater, "Suv7Seater")]
    [InlineData(VehicleType.Van7Seater, "Van7Seater")]
    public void VehicleType_SerializesAsStableCaseSensitiveString(
        VehicleType vehicleType,
        string expected)
    {
        var payload = JsonSerializer.Serialize(vehicleType, JsonOptions);

        payload.Should().Be($"\"{expected}\"");
        JsonSerializer.Deserialize<VehicleType>(payload, JsonOptions).Should().Be(vehicleType);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("\"Car\"")]
    [InlineData("\"sedan\"")]
    [InlineData("\"SUV\"")]
    public void VehicleType_RejectsNumericLegacyAndIncorrectCaseValues(string payload)
    {
        var action = () => JsonSerializer.Deserialize<VehicleType>(payload, JsonOptions);

        action.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("""{"vehicleType":"Sedan"}""", JsonValueKind.Null)]
    [InlineData(
        """{"vehicleType":"Sedan","imageUrl":"https://cdn.example.test/vehicles/sedan.png"}""",
        JsonValueKind.String)]
    public void ReservationVehicleSnapshot_RoundTripsNullableImageAndIgnoresUnknownProperties(
        string payload,
        JsonValueKind expectedImageKind)
    {
        var extendedPayload = payload.TrimEnd('}') + ""","quotedVehiclePrice":0.01,"eligible":false}""";

        var snapshot = JsonSerializer.Deserialize<ReservationVehicleSnapshot>(
            extendedPayload,
            JsonOptions);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, JsonOptions));

        snapshot.Should().NotBeNull();
        snapshot!.VehicleType.Should().Be(VehicleType.Sedan);
        document.RootElement.GetProperty("imageUrl").ValueKind.Should().Be(expectedImageKind);
        document.RootElement.TryGetProperty("quotedVehiclePrice", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("eligible", out _).Should().BeFalse();
    }

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
            Vehicle = new ReservationVehicleSnapshot
            {
                VehicleType = VehicleType.Sedan,
                ImageUrl = "https://cdn.example.test/vehicles/sedan.png"
            },
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
        roundTrip.Vehicle.VehicleType.Should().Be(VehicleType.Sedan);
        roundTrip.Vehicle.ImageUrl.Should().Be("https://cdn.example.test/vehicles/sedan.png");
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

    [Fact]
    public void CatalogSnapshotOffering_RoundTripsQualifierAndStringBadge()
    {
        var offering = new CatalogSnapshotOffering
        {
            Id = Guid.NewGuid(),
            NameAr = "غسيل كامل",
            QualifierAr = "بدون التعقيم",
            QualifierHe = null,
            BadgeCode = CatalogOfferingBadgeCode.MostRequested,
            BasePrice = 100m,
            DurationMinutes = 45
        };

        var payload = JsonSerializer.Serialize(offering, JsonOptions);
        using var document = JsonDocument.Parse(payload);
        var roundTripped = JsonSerializer.Deserialize<CatalogSnapshotOffering>(payload, JsonOptions);

        document.RootElement.GetProperty("qualifierAr").GetString().Should().Be("بدون التعقيم");
        document.RootElement.GetProperty("qualifierHe").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("badgeCode").GetString().Should().Be("MostRequested");
        roundTripped.Should().NotBeNull();
        roundTripped!.BadgeCode.Should().Be(CatalogOfferingBadgeCode.MostRequested);
    }

    [Fact]
    public void CatalogSnapshotOffering_RejectsIntegerBadge()
    {
        const string payload =
            """{"id":"11111111-1111-1111-1111-111111111111","nameAr":"غسيل","basePrice":10,"durationMinutes":30,"badgeCode":0}""";

        var action = () => JsonSerializer.Deserialize<CatalogSnapshotOffering>(payload, JsonOptions);

        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void CatalogSnapshotOffering_RejectsUnknownBadge()
    {
        const string payload =
            """{"id":"11111111-1111-1111-1111-111111111111","nameAr":"غسيل","basePrice":10,"durationMinutes":30,"badgeCode":"Popular"}""";

        var action = () => JsonSerializer.Deserialize<CatalogSnapshotOffering>(payload, JsonOptions);

        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void AvailabilityDiscoveryContracts_UseExactCamelCaseAndIgnoreUnknownFields()
    {
        var companyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var branchId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var request = new AvailabilityDiscoveryRequest
        {
            Date = new DateOnly(2026, 9, 28),
            PreferredLocalTime = new TimeOnly(10, 30),
            Candidates =
            [
                new AvailabilityDiscoveryCompanyCandidate
                {
                    CompanyId = companyId,
                    BranchIds = [branchId]
                }
            ]
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var withUnknown = json.TrimEnd('}') + ""","unknownField":"ignored"}""";
        var roundTrip = JsonSerializer.Deserialize<AvailabilityDiscoveryRequest>(
            withUnknown,
            JsonOptions);

        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal(
                "contractVersion",
                "date",
                "preferredLocalTime",
                "customerLocation",
                "candidates");
        document.RootElement.GetProperty("candidates")[0]
            .EnumerateObject().Select(property => property.Name)
            .Should().Equal("companyId", "branchIds");
        roundTrip!.Candidates.Single().CompanyId.Should().Be(companyId);
        JsonSerializer.Serialize(roundTrip, JsonOptions)
            .Should().NotContain("unknownField");
    }

    [Fact]
    public void AvailabilityEnums_RejectNumericValues()
    {
        const string payload =
            """{"dayOfWeek":1,"startLocalTime":"09:00:00","endLocalTime":"10:00:00","slotDurationMinutes":30,"capacity":1}""";
        var action = () => JsonSerializer.Deserialize<CatalogSnapshotRecurringSchedule>(
            payload,
            JsonOptions);

        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void CatalogSnapshotCategory_RoundTripsPresentationMetadata()
    {
        var category = new CatalogSnapshotCategory
        {
            Id = Guid.NewGuid(),
            NameAr = "غسيل",
            ImageUrl = "https://cdn.example.test/categories/exterior.png",
            ColorHex = "#1A73E8"
        };

        var payload = JsonSerializer.Serialize(category, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<CatalogSnapshotCategory>(payload, JsonOptions);

        roundTripped!.ImageUrl.Should().Be(category.ImageUrl);
        roundTripped.ColorHex.Should().Be(category.ColorHex);
    }

    [Fact]
    public void CatalogSnapshotCategory_RoundTripsExplicitNullPresentationMetadata()
    {
        var category = new CatalogSnapshotCategory
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            NameAr = "غسيل",
            ImageUrl = null,
            ColorHex = null
        };

        var payload = JsonSerializer.Serialize(category, JsonOptions);
        using var document = JsonDocument.Parse(payload);
        var roundTripped = JsonSerializer.Deserialize<CatalogSnapshotCategory>(payload, JsonOptions);

        document.RootElement.GetProperty("imageUrl").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("colorHex").ValueKind.Should().Be(JsonValueKind.Null);
        roundTripped!.ImageUrl.Should().BeNull();
        roundTripped.ColorHex.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","nameAr":"غسيل","imageUrl":42}""")]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","nameAr":"غسيل","colorHex":false}""")]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","nameAr":"غسيل","imageUrl":{"url":"https://example.test/a.png"}}""")]
    public void CatalogSnapshotCategory_RejectsInvalidPresentationWireTypes(string payload)
    {
        var action = () => JsonSerializer.Deserialize<CatalogSnapshotCategory>(payload, JsonOptions);

        action.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","nameAr":"غسيل","basePrice":10,"durationMinutes":30}""")]
    [InlineData("""{"id":"11111111-1111-1111-1111-111111111111","nameAr":"غسيل","basePrice":10,"durationMinutes":30,"badgeCode":null}""")]
    public void CatalogSnapshotOffering_AllowsMissingOrNullBadge(string payload)
    {
        var offering = JsonSerializer.Deserialize<CatalogSnapshotOffering>(payload, JsonOptions);

        offering.Should().NotBeNull();
        offering!.BadgeCode.Should().BeNull();
    }
}
