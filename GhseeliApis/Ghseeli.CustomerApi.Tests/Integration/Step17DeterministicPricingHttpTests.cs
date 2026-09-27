using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.Vehicles;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Proves authoritative pricing and unavailable outcomes at the Customer HTTP boundary.
/// </summary>
public sealed class Step17DeterministicPricingHttpTests
{
    [Fact]
    public async Task DirectReprice_AllVehicleTypesHaveIdenticalEligibilityAndPrice()
    {
        var snapshot = CreateSnapshot(version: 14, basePrice: 120m);
        var token = CatalogTestSupport.CreateToken(180);
        var device = CatalogTestSupport.CreateDevice(
            token,
            Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory([device], snapshot);
        using var client = factory.CreateApiClient();
        await MakeCatalogFreshAsync(factory);
        var totals = new List<decimal>();

        foreach (var vehicleType in Enum.GetValues<VehicleType>())
        {
            var body = CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                Step17CustomerHttpTestSupport.FixedNow.AddHours(2));
            body.Vehicle.VehicleType = vehicleType;
            using var request = Step17CustomerHttpTestSupport.Request(
                HttpMethod.Post,
                "/api/v1/pricing/reprice?language=ar",
                token,
                JsonContent.Create(body),
                correlationId: $"corr-vehicle-{vehicleType}");
            using var response = await client.SendAsync(request);
            var quote = await response.Content.ReadFromJsonAsync<DirectCheckoutPricingResponse>();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            quote.Should().NotBeNull();
            quote!.Intent.Vehicle.VehicleType.Should().Be(vehicleType);
            quote.Intent.Vehicle.ImageUrl.Should()
                .Be("https://cdn.example.test/vehicles/sedan.png");
            totals.Add(quote.Pricing.GrandTotal);
        }

        totals.Should().OnlyContain(value => value == totals[0]);
    }

    [Fact]
    public async Task DirectReprice_UnknownVehiclePropertiesAreHarmless()
    {
        var snapshot = CreateSnapshot(version: 14, basePrice: 120m);
        var token = CatalogTestSupport.CreateToken(179);
        var device = CatalogTestSupport.CreateDevice(
            token,
            Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory([device], snapshot);
        using var client = factory.CreateApiClient();
        await MakeCatalogFreshAsync(factory);
        var body = JsonSerializer.SerializeToNode(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                Step17CustomerHttpTestSupport.FixedNow.AddHours(2)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        body["vehicle"]!["quotedVehiclePrice"] = 0.01m;
        body["vehicle"]!["eligible"] = false;
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/pricing/reprice?language=ar",
            token,
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            correlationId: "corr-vehicle-unknown-properties");

        using var response = await client.SendAsync(request);
        var quote = await response.Content.ReadFromJsonAsync<DirectCheckoutPricingResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        quote!.Pricing.GrandTotal.Should().Be(120m);
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("\"Car\"", null)]
    [InlineData("\"sedan\"", null)]
    [InlineData("\"Sedan\"", "http://cdn.example.test/vehicle.png")]
    [InlineData("\"Sedan\"", "/images/vehicle.png")]
    [InlineData("\"Sedan\"", "https://user:password@cdn.example.test/vehicle.png")]
    [InlineData("\"Sedan\"", "not-a-url")]
    public async Task DirectReprice_RejectsInvalidVehicleEnumOrImage(
        string vehicleTypeJson,
        string? imageUrl)
    {
        var snapshot = CreateSnapshot(version: 14, basePrice: 120m);
        var token = CatalogTestSupport.CreateToken(178);
        var device = CatalogTestSupport.CreateDevice(
            token,
            Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory([device], snapshot);
        using var client = factory.CreateApiClient();
        await MakeCatalogFreshAsync(factory);
        var body = JsonSerializer.SerializeToNode(
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                Step17CustomerHttpTestSupport.FixedNow.AddHours(2)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        body["vehicle"]!["vehicleType"] = JsonNode.Parse(vehicleTypeJson);
        if (imageUrl is not null)
        {
            body["vehicle"]!["imageUrl"] = imageUrl;
        }
        var before = await ReadCustomerWriteStateAsync(factory);

        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/pricing/reprice?language=ar",
            token,
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            correlationId: "corr-vehicle-invalid");
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadCustomerWriteStateAsync(factory)).Should().Be(before);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-PRICE-015")]
    [Trait("ScenarioId", "FAN-VEHICLE-PRICING-010")]
    public async Task STEP17_DET_PRICE_015_DirectRepriceUsesUpdatedBusinessPrice()
    {
        var snapshot = CreateSnapshot(version: 15, basePrice: 100m);
        var token = CatalogTestSupport.CreateToken(181);
        var device = CatalogTestSupport.CreateDevice(token, Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory([device], snapshot);
        using var client = factory.CreateApiClient();
        await MakeCatalogFreshAsync(factory);

        snapshot.Categories.Single().Offerings.Single().BasePrice = 120m;
        var before = await ReadCustomerWriteStateAsync(factory);
        using var request = DirectRepriceRequest(snapshot, token, "corr-step17-det-price-015");

        using var response = await client.SendAsync(request);
        await AssertJsonSuccessAsync(response, "corr-step17-det-price-015");
        var quote = await response.Content.ReadFromJsonAsync<DirectCheckoutPricingResponse>();

        quote.Should().NotBeNull();
        quote!.Pricing.CatalogVersion.Should().Be(15);
        quote.Pricing.Currency.Should().Be("ILS");
        quote.Pricing.BaseSubtotal.Should().Be(120m);
        quote.Pricing.AddonSubtotal.Should().Be(0m);
        quote.Pricing.ItemSubtotal.Should().Be(120m);
        quote.Pricing.ServiceFee.Should().Be(0m);
        quote.Pricing.Tax.Should().Be(0m);
        quote.Pricing.GrandTotal.Should().Be(120m);
        quote.Pricing.Items.Should().ContainSingle();
        quote.Pricing.Items.Single().BaseSubtotal.Should().Be(120m);
        quote.Pricing.Items.Single().ItemSubtotal.Should().Be(120m);
        quote.Intent.Vehicle.VehicleType.Should()
            .Be(Ghseeli.IntegrationContracts.Vehicles.VehicleType.Sedan);
        quote.Intent.Vehicle.ImageUrl.Should()
            .Be("https://cdn.example.test/vehicles/sedan.png");
        factory.BusinessApiClient.ValidateAppointmentRequests.Should().Be(1);
        factory.BusinessApiClient.CatalogSnapshotRequests.Should().Be(0);
        (await ReadCustomerWriteStateAsync(factory)).Should().Be(before);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.CatalogOfferings.AsNoTracking().SingleAsync()).BasePrice.Should().Be(100m);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-PRICE-016")]
    public async Task STEP17_DET_PRICE_016_RepeatedStaleMapsToUnavailable()
    {
        var initial = CreateSnapshot(version: 16, basePrice: 100m);
        var refreshed = CatalogTestSupport.CreateSnapshot(
            initial.Company.Id,
            version: 17,
            branchId: initial.Branches.Single().Id,
            categoryId: initial.Categories.Single().Id,
            offeringId: initial.Categories.Single().Offerings.Single().Id,
            addonGroupId: initial.Categories.Single().Offerings.Single().AddonGroups.Single().Id,
            addonChoiceId: initial.Categories.Single().Offerings.Single().AddonGroups.Single().Choices.Single().Id);
        refreshed.Categories.Single().Offerings.Single().BasePrice = 100m;
        var token = CatalogTestSupport.CreateToken(182);
        var device = CatalogTestSupport.CreateDevice(token, Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory([device], initial);
        using var client = factory.CreateApiClient();
        await MakeCatalogFreshAsync(factory);

        factory.BusinessApiClient.GetCatalogSnapshotHandler = (_, _) => Task.FromResult(refreshed);
        factory.BusinessApiClient.ValidateAppointmentHandler = (validationRequest, _, _) =>
            Task.FromResult(CatalogTestSupport.CreateValidationResponse(
                validationRequest.ExpectedCatalogVersion == 16 ? initial : refreshed,
                validationRequest,
                catalogVersion: validationRequest.ExpectedCatalogVersion + 1,
                errors:
                [
                    new AppointmentValidationIssue
                    {
                        Code = AppointmentValidationErrorCodes.StaleCatalogVersion,
                        Field = "expectedCatalogVersion",
                        Message = "The supplied catalog version is stale."
                    }
                ]));

        var before = await ReadCustomerWriteStateAsync(factory);
        using var request = DirectRepriceRequest(initial, token, "corr-step17-det-price-016");
        using var response = await client.SendAsync(request);

        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            CheckoutPricingProblemCodes.Unavailable,
            "ar",
            "التسعير المعتمد غير متاح حالياً.",
            "تعذر الحصول على تسعير معتمد حالياً. حاول مرة أخرى لاحقاً.",
            "corr-step17-det-price-016");
        factory.BusinessApiClient.ValidateAppointmentRequests.Should().Be(2);
        factory.BusinessApiClient.CatalogSnapshotRequests.Should().Be(1);
        factory.BusinessApiClient.ValidationRequests
            .Select(entry => entry.Request.ExpectedCatalogVersion)
            .Should().Equal(16, 17);
        (await ReadCustomerWriteStateAsync(factory)).Should().Be(before);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-PRICE-017")]
    [Trait("ScenarioId", "FAN-OPTIONAL-UPSTREAM-041")]
    public async Task STEP17_DET_PRICE_017_DirectRepriceUnavailableIsStable()
    {
        var snapshot = CreateSnapshot(version: 17, basePrice: 100m);
        var token = CatalogTestSupport.CreateToken(183);
        var device = CatalogTestSupport.CreateDevice(token, Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory([device], snapshot);
        using var client = factory.CreateApiClient();
        await MakeCatalogFreshAsync(factory);
        factory.BusinessApiClient.ValidateAppointmentHandler = (_, _, _) =>
            Task.FromException<ValidateAppointmentResponse>(
                new BusinessApiUnavailableException("unavailable", "corr-business-price-017"));

        var before = await ReadCustomerWriteStateAsync(factory);
        using var request = DirectRepriceRequest(snapshot, null, "corr-step17-det-price-017");
        using var response = await client.SendAsync(request);

        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            CheckoutPricingProblemCodes.Unavailable,
            "ar",
            "التسعير المعتمد غير متاح حالياً.",
            "تعذر الحصول على تسعير معتمد حالياً. حاول مرة أخرى لاحقاً.",
            "corr-step17-det-price-017");
        factory.BusinessApiClient.ValidateAppointmentRequests.Should().Be(1);
        factory.BusinessApiClient.CatalogSnapshotRequests.Should().Be(0);
        (await ReadCustomerWriteStateAsync(factory)).Should().Be(before);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-PRICE-018")]
    public async Task STEP17_DET_PRICE_018_DraftRepriceUnavailablePreservesState()
    {
        var snapshot = CreateSnapshot(version: 18, basePrice: 100m);
        var token = CatalogTestSupport.CreateToken(184);
        var device = CatalogTestSupport.CreateDevice(token, Step17CustomerHttpTestSupport.FixedNow.AddDays(1));
        await using var factory = Step17CustomerHttpTestSupport.CreateFactory([device], snapshot);
        using var client = factory.CreateApiClient();
        await MakeCatalogFreshAsync(factory);
        var orderGuid = await Step17CustomerHttpTestSupport.CreateDraftAsync(factory, client, token);
        var version = await Step17CustomerHttpTestSupport.RepriceDraftAsync(client, token, orderGuid, 1);
        version.Should().Be(2);

        factory.BusinessApiClient.ValidateAppointmentHandler = (_, _, _) =>
            Task.FromException<ValidateAppointmentResponse>(
                new BusinessApiUnavailableException("unavailable", "corr-business-price-018"));
        var before = await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid);
        var validationCallsBefore = factory.BusinessApiClient.ValidateAppointmentRequests;
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/checkout/reprice?language=ar",
            token,
            JsonContent.Create(new RepriceCheckoutDraftRequest
            {
                ExpectedVersion = version
            }),
            correlationId: "corr-step17-det-price-018");
        request.Headers.TryAddWithoutValidation("X-Order-Guid", orderGuid.ToString("D"));

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            CheckoutPricingProblemCodes.Unavailable,
            "ar",
            "التسعير المعتمد غير متاح حالياً.",
            "تعذر الحصول على تسعير معتمد حالياً. حاول مرة أخرى لاحقاً.",
            "corr-step17-det-price-018");
        factory.BusinessApiClient.ValidateAppointmentRequests.Should().Be(validationCallsBefore + 1);
        (await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid))
            .Should().Be(before);
    }

    private static CatalogSnapshotResponse CreateSnapshot(long version, decimal basePrice)
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version);
        snapshot.Categories.Single().Offerings.Single().BasePrice = basePrice;
        return snapshot;
    }

    private static HttpRequestMessage DirectRepriceRequest(
        CatalogSnapshotResponse snapshot,
        string? token,
        string correlationId) =>
        Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/pricing/reprice?language=ar",
            token,
            JsonContent.Create(CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                Step17CustomerHttpTestSupport.FixedNow.AddHours(2))),
            correlationId: correlationId);

    private static async Task AssertJsonSuccessAsync(
        HttpResponseMessage response,
        string correlationId)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle(correlationId);
        response.Content.Headers.ContentLanguage.Should().ContainSingle("ar");
        response.Headers.Vary.Should().Contain("Accept-Language");
        Step17CustomerHttpTestSupport.AssertSafeHeaders(response);
    }

    private static async Task MakeCatalogFreshAsync(CheckoutDraftApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var provider = await context.CatalogProviders.SingleAsync();
        provider.LastSuccessfulRefreshAtUtc = Step17CustomerHttpTestSupport.FixedNow;
        provider.LastAttemptedRefreshAtUtc = Step17CustomerHttpTestSupport.FixedNow;
        await context.SaveChangesAsync();
    }

    private static async Task<string> ReadCustomerWriteStateAsync(CheckoutDraftApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return JsonSerializer.Serialize(new
        {
            Drafts = await context.CheckoutDrafts.CountAsync(),
            DraftItems = await context.CheckoutDraftItems.CountAsync(),
            DraftSelections = await context.CheckoutDraftSelections.CountAsync(),
            PricingSnapshots = await context.CheckoutDraftPricingSnapshots.CountAsync(),
            PricingItems = await context.CheckoutDraftPricingItemSnapshots.CountAsync(),
            PricingSelections = await context.CheckoutDraftPricingSelectionSnapshots.CountAsync(),
            Bookings = await context.CustomerBookings.CountAsync(),
            BookingItems = await context.CustomerBookingItems.CountAsync(),
            BookingSelections = await context.CustomerBookingSelections.CountAsync(),
            Attempts = await context.BookingConfirmationAttempts.CountAsync(),
            Payments = await context.CustomerPayments.CountAsync(),
            PaymentIdempotency = await context.CustomerPaymentIdempotencyRecords.CountAsync()
        });
    }
}
