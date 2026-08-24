using FluentAssertions;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Devices;
using GhseeliApis.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Exercises the real customer HTTP pipeline for anonymous checkout drafts.
/// </summary>
public class CheckoutDraftApiIntegrationTests
{
    [Fact]
    public async Task PostDraft_WithoutDeviceToken_ReturnsLocalizedUnauthorizedProblem()
    {
        await using var factory = new CheckoutDraftApiFactory();
        using var client = factory.CreateApiClient();
        var snapshot = factory.Snapshot;

        using var response = await client.PostAsJsonAsync(
            "/api/v1/checkout/drafts?language=he",
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString().Should().Be(DeviceProblemCodes.TokenMissing);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-DRAFT-CREATE-065")]
    [Trait("ScenarioId", "STEP15-CUSTOMER-DRAFT-READ-066")]
    [Trait("ScenarioId", "STEP15-CUSTOMER-DRAFT-UPDATE-067")]
    public async Task DraftLifecycle_WithValidDeviceToken_CreateGetAndUpdateRoundTripsSuccessfully()
    {
        var token = CatalogTestSupport.CreateToken(31);
        var device = CatalogTestSupport.CreateDevice(
            token,
            expiresAt: new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero));
        await using var factory = new CheckoutDraftApiFactory(devices: [device]);
        using var client = factory.CreateApiClient();

        var createRequest = CheckoutDraftTestSupport.CreateValidCreateRequest(
            factory.Snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        using var createMessage = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/drafts?language=he",
            token,
            "ar");
        createMessage.Content = JsonContent.Create(createRequest);

        using var createResponse = await client.SendAsync(createMessage);
        using var createDocument = await ReadJsonAsync(createResponse);
        var orderGuid = createDocument.RootElement.GetProperty("orderGuid").GetGuid();
        var firstExpiry = createDocument.RootElement.GetProperty("expiresAt").GetDateTimeOffset();

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        createResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        createDocument.RootElement.GetProperty("language").GetString().Should().Be("he");
        createDocument.RootElement.GetProperty("version").GetInt32().Should().Be(1);
        createDocument.RootElement.GetProperty("requiresReprice").GetBoolean().Should().BeTrue();
        createDocument.RootElement.GetProperty("intent")
            .GetProperty("requestedSlotStartUtc")
            .GetDateTimeOffset()
            .Should()
            .Be(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));

        using var getResponse = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=ar",
            token));
        using var getDocument = await ReadJsonAsync(getResponse);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        getDocument.RootElement.GetProperty("language").GetString().Should().Be("ar");
        getDocument.RootElement.GetProperty("orderGuid").GetGuid().Should().Be(orderGuid);

        factory.TimeProvider.Advance(TimeSpan.FromMinutes(5));
        var updateRequest = CheckoutDraftTestSupport.CreateValidUpdateRequest(
            factory.Snapshot,
            new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
            expectedVersion: 1);
        updateRequest.Vehicle.Color = "Green";
        using var updateMessage = CreateRequest(
            HttpMethod.Put,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=ar",
            token);
        updateMessage.Content = JsonContent.Create(updateRequest);

        using var updateResponse = await client.SendAsync(updateMessage);
        using var updateDocument = await ReadJsonAsync(updateResponse);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        updateResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        updateDocument.RootElement.GetProperty("version").GetInt32().Should().Be(2);
        updateDocument.RootElement.GetProperty("intent").GetProperty("vehicle").GetProperty("color").GetString()
            .Should()
            .Be("Green");
        updateDocument.RootElement.GetProperty("expiresAt").GetDateTimeOffset()
            .Should()
            .BeAfter(firstExpiry);
    }

    [Fact]
    public async Task DraftLifecycle_WithFixedIncludedSelection_RoundTripsGetIntentBackIntoPut()
    {
        var token = CatalogTestSupport.CreateToken(31);
        var snapshot = CreateFixedIncludedSnapshot(Guid.NewGuid());
        var device = CatalogTestSupport.CreateDevice(
            token,
            expiresAt: new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero));
        await using var factory = new CheckoutDraftApiFactory(snapshot, [device]);
        using var client = factory.CreateApiClient();

        var createRequest = CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        using var createMessage = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/drafts?language=ar",
            token,
            "he");
        createMessage.Content = JsonContent.Create(createRequest);

        using var createResponse = await client.SendAsync(createMessage);
        using var createDocument = await ReadJsonAsync(createResponse);
        var orderGuid = createDocument.RootElement.GetProperty("orderGuid").GetGuid();

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        createDocument.RootElement.GetProperty("intent")
            .GetProperty("items")[0]
            .GetProperty("selections")
            .GetArrayLength()
            .Should()
            .Be(1);

        using var getResponse = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=he",
            token));
        using var getDocument = await ReadJsonAsync(getResponse);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getItem = getDocument.RootElement.GetProperty("intent").GetProperty("items")[0];
        var getSelection = getItem.GetProperty("selections")[0];
        var updateRequest = CheckoutDraftTestSupport.CreateValidUpdateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
            expectedVersion: getDocument.RootElement.GetProperty("version").GetInt32());
        updateRequest.Items =
        [
            new CheckoutDraftItemRequest
            {
                OfferingSourceId = getItem.GetProperty("offeringSourceId").GetGuid(),
                Selections =
                [
                    new CheckoutDraftSelectionRequest
                    {
                        AddonChoiceSourceId = getSelection.GetProperty("addonChoiceSourceId").GetGuid(),
                        Quantity = getSelection.GetProperty("quantity").GetInt32()
                    }
                ]
            }
        ];

        using var updateMessage = CreateRequest(
            HttpMethod.Put,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=he",
            token);
        updateMessage.Content = JsonContent.Create(updateRequest);

        using var updateResponse = await client.SendAsync(updateMessage);
        using var updateDocument = await ReadJsonAsync(updateResponse);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        updateDocument.RootElement.GetProperty("version").GetInt32().Should().Be(2);
        updateDocument.RootElement.GetProperty("intent")
            .GetProperty("items")[0]
            .GetProperty("selections")[0]
            .GetProperty("quantity")
            .GetInt32()
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task PostDraft_WithIdenticalPayloadTwice_ReturnsDistinctOrderGuids()
    {
        var token = CatalogTestSupport.CreateToken(35);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            factory.Snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));

        using var firstMessage = CreateRequest(HttpMethod.Post, "/api/v1/checkout/drafts?language=he", token);
        firstMessage.Content = JsonContent.Create(request);
        using var firstResponse = await client.SendAsync(firstMessage);
        using var firstDocument = await ReadJsonAsync(firstResponse);

        using var secondMessage = CreateRequest(HttpMethod.Post, "/api/v1/checkout/drafts?language=he", token);
        secondMessage.Content = JsonContent.Create(request);
        using var secondResponse = await client.SendAsync(secondMessage);
        using var secondDocument = await ReadJsonAsync(secondResponse);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        firstDocument.RootElement.GetProperty("orderGuid").GetGuid()
            .Should()
            .NotBe(secondDocument.RootElement.GetProperty("orderGuid").GetGuid());
        firstDocument.RootElement.GetProperty("version").GetInt32().Should().Be(1);
        secondDocument.RootElement.GetProperty("version").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetDraft_FromWrongDevice_ReturnsSameNotFoundProblemAsMissing()
    {
        var firstToken = CatalogTestSupport.CreateToken(32);
        var secondToken = CatalogTestSupport.CreateToken(33);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(firstToken, expiresAt: DateTimeOffset.UtcNow.AddDays(30)),
            CatalogTestSupport.CreateDevice(secondToken, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var orderGuid = await CreateDraftAsync(client, factory.Snapshot, firstToken);

        using var wrongDeviceResponse = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=he",
            secondToken));
        using var wrongDeviceDocument = await ReadJsonAsync(wrongDeviceResponse);

        using var missingResponse = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/v1/checkout/drafts/{Guid.NewGuid():D}?language=he",
            secondToken));
        using var missingDocument = await ReadJsonAsync(missingResponse);

        wrongDeviceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongDeviceDocument.RootElement.GetProperty("code").GetString()
            .Should()
            .Be(missingDocument.RootElement.GetProperty("code").GetString());
        wrongDeviceDocument.RootElement.GetProperty("detail").GetString()
            .Should()
            .Be(missingDocument.RootElement.GetProperty("detail").GetString());
        wrongDeviceDocument.RootElement.GetProperty("language").GetString()
            .Should()
            .Be(missingDocument.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task GetDraft_WhenExpired_ReturnsGoneWithoutExtendingLifetime()
    {
        var token = CatalogTestSupport.CreateToken(34);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var orderGuid = await CreateDraftAsync(client, factory.Snapshot, token);
        factory.TimeProvider.Advance(TimeSpan.FromMinutes(30));

        using var response = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/v1/checkout/drafts/{orderGuid:D}",
            token));
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        document.RootElement.GetProperty("code").GetString().Should().Be(CheckoutDraftProblemCodes.Expired);
    }

    [Fact]
    public async Task PostDraft_WhenPayloadExceedsEndpointRequestLimit_ReturnsPayloadTooLarge()
    {
        var token = CatalogTestSupport.CreateToken(35);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        using var message = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/drafts?language=ar",
            token);
        message.Content = new StringContent(
            BuildOversizedCreatePayload(factory.Snapshot),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(message);

        AssertDraftPayloadTooLargeResponse(response);
    }

    [Fact]
    public async Task PutDraft_WhenPayloadExceedsEndpointRequestLimit_ReturnsPayloadTooLarge()
    {
        var token = CatalogTestSupport.CreateToken(36);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var orderGuid = await CreateDraftAsync(client, factory.Snapshot, token);
        using var message = CreateRequest(
            HttpMethod.Put,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=ar",
            token);
        message.Content = new StringContent(
            BuildOversizedUpdatePayload(factory.Snapshot),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(message);

        AssertDraftPayloadTooLargeResponse(response);
    }

    [Fact]
    public async Task PutDraft_FromWrongDevice_ReturnsSameNotFoundProblemAsMissing()
    {
        var firstToken = CatalogTestSupport.CreateToken(37);
        var secondToken = CatalogTestSupport.CreateToken(38);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(firstToken, expiresAt: DateTimeOffset.UtcNow.AddDays(30)),
            CatalogTestSupport.CreateDevice(secondToken, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var orderGuid = await CreateDraftAsync(client, factory.Snapshot, firstToken);
        var request = CheckoutDraftTestSupport.CreateValidUpdateRequest(
            factory.Snapshot,
            new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
            expectedVersion: 1);

        using var wrongDeviceRequest = CreateRequest(
            HttpMethod.Put,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=he",
            secondToken);
        wrongDeviceRequest.Content = JsonContent.Create(request);
        using var wrongDeviceResponse = await client.SendAsync(wrongDeviceRequest);
        using var wrongDeviceDocument = await ReadJsonAsync(wrongDeviceResponse);

        using var missingRequest = CreateRequest(
            HttpMethod.Put,
            $"/api/v1/checkout/drafts/{Guid.NewGuid():D}?language=he",
            secondToken);
        missingRequest.Content = JsonContent.Create(request);
        using var missingResponse = await client.SendAsync(missingRequest);
        using var missingDocument = await ReadJsonAsync(missingResponse);

        wrongDeviceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongDeviceDocument.RootElement.GetProperty("code").GetString()
            .Should()
            .Be(missingDocument.RootElement.GetProperty("code").GetString());
        wrongDeviceDocument.RootElement.GetProperty("detail").GetString()
            .Should()
            .Be(missingDocument.RootElement.GetProperty("detail").GetString());
        wrongDeviceDocument.RootElement.GetProperty("language").GetString()
            .Should()
            .Be(missingDocument.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task PutDraft_WithStaleExpectedVersion_ReturnsStableConflict()
    {
        var token = CatalogTestSupport.CreateToken(39);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var orderGuid = await CreateDraftAsync(client, factory.Snapshot, token);
        var request = CheckoutDraftTestSupport.CreateValidUpdateRequest(
            factory.Snapshot,
            new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
            expectedVersion: 1);

        using var firstUpdateMessage = CreateRequest(
            HttpMethod.Put,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=ar",
            token);
        firstUpdateMessage.Content = JsonContent.Create(request);
        using var firstUpdateResponse = await client.SendAsync(firstUpdateMessage);

        using var staleUpdateMessage = CreateRequest(
            HttpMethod.Put,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=ar",
            token);
        staleUpdateMessage.Content = JsonContent.Create(request);
        using var staleUpdateResponse = await client.SendAsync(staleUpdateMessage);
        using var staleDocument = await ReadJsonAsync(staleUpdateResponse);

        firstUpdateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        staleUpdateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        staleDocument.RootElement.GetProperty("code").GetString()
            .Should()
            .Be(CheckoutDraftProblemCodes.VersionConflict);
    }

    [Fact]
    public async Task PostDraft_WithZeroWidthVehicleAndLocationText_ReturnsLocalizedValidationProblem()
    {
        var token = CatalogTestSupport.CreateToken(40);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var request = CheckoutDraftTestSupport.CreateValidCreateRequest(
            factory.Snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        request.Vehicle.VehicleType = "\u200B";
        request.Location.AddressLine = "\u200B";

        using var message = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/drafts?language=ar",
            token,
            "he");
        message.Content = JsonContent.Create(request);

        using var response = await client.SendAsync(message);
        using var document = await ReadJsonAsync(response);
        var errors = document.RootElement.GetProperty("fieldErrors");
        var errorKeys = errors.EnumerateObject().Select(property => property.Name).ToArray();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString().Should().Be(CheckoutDraftProblemCodes.Invalid);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        errorKeys.Should().Contain(key => key.Contains("vehicle", StringComparison.OrdinalIgnoreCase));
        errorKeys.Should().Contain(key => key.Contains("location", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PostDraft_WithNullItemElement_ReturnsLocalizedValidationProblemAtStablePath()
    {
        var token = CatalogTestSupport.CreateToken(42);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var payload = $$"""
        {
          "businessSourceId": "{{factory.Snapshot.Company.Id}}",
          "branchSourceId": "{{factory.Snapshot.Branches.Single().Id}}",
          "requestedSlotStartUtc": "2026-08-24T10:00:00Z",
          "vehicle": {
            "vehicleType": "Sedan"
          },
          "location": {
            "addressLine": "الشارع الرئيسي 10",
            "latitude": 32.1,
            "longitude": 34.8
          },
          "items": [null]
        }
        """;

        using var message = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/drafts",
            token,
            "he");
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(message);
        using var document = await ReadJsonAsync(response);
        var fieldErrors = document.RootElement.GetProperty("fieldErrors");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString().Should().Be(CheckoutDraftProblemCodes.Invalid);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        fieldErrors.TryGetProperty("items[0]", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PostDraft_WithNullSelectionElement_ReturnsLocalizedValidationProblemAtStablePath()
    {
        var token = CatalogTestSupport.CreateToken(43);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var payload = $$"""
        {
          "businessSourceId": "{{factory.Snapshot.Company.Id}}",
          "branchSourceId": "{{factory.Snapshot.Branches.Single().Id}}",
          "requestedSlotStartUtc": "2026-08-24T10:00:00Z",
          "vehicle": {
            "vehicleType": "Sedan"
          },
          "location": {
            "addressLine": "الشارع الرئيسي 10",
            "latitude": 32.1,
            "longitude": 34.8
          },
          "items": [
            {
              "offeringSourceId": "{{factory.Snapshot.Categories.Single().Offerings.Single().Id}}",
              "selections": [null]
            }
          ]
        }
        """;

        using var message = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/drafts",
            token,
            "he");
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(message);
        using var document = await ReadJsonAsync(response);
        var fieldErrors = document.RootElement.GetProperty("fieldErrors");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString().Should().Be(CheckoutDraftProblemCodes.Invalid);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        fieldErrors.TryGetProperty("items[0].selections[0]", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PostDraft_WithForgedPriceFields_IgnoresThemAndStillRequiresReprice()
    {
        var token = CatalogTestSupport.CreateToken(41);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var branch = factory.Snapshot.Branches.Single();
        var offering = factory.Snapshot.Categories.Single().Offerings.First();
        var payload = $$"""
        {
          "businessSourceId": "{{factory.Snapshot.Company.Id}}",
          "branchSourceId": "{{branch.Id}}",
          "requestedSlotStartUtc": "2026-08-24T10:00:00Z",
          "quotedSubtotal": 0.01,
          "quotedTotal": 0.02,
          "vehicle": {
            "vehicleType": "Sedan",
            "licensePlate": "12-345-67",
            "quotedVehiclePrice": 0.03
          },
          "location": {
            "addressLine": "الشارع الرئيسي 10",
            "city": "حيفا",
            "area": "الكرمل",
            "latitude": 32.1,
            "longitude": 34.81,
            "quotedServiceFee": 0.04
          },
          "items": [
            {
              "offeringSourceId": "{{offering.Id}}",
              "quotedBasePrice": 0.05,
              "selections": []
            }
          ]
        }
        """;

        using var message = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/drafts?language=ar",
            token);
        message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(message);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("requiresReprice").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("intent").GetProperty("catalogVersion").GetInt64()
            .Should()
            .Be(factory.Snapshot.CatalogVersion);
        document.RootElement.TryGetProperty("quotedTotal", out _).Should().BeFalse();
        document.RootElement.GetProperty("intent").TryGetProperty("quotedSubtotal", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Swagger_ContainsCheckoutDraftRoutes_AndServicesResolve()
    {
        await using var factory = new CheckoutDraftApiFactory();
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = await ReadJsonAsync(response);
        using var scope = factory.Services.CreateScope();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paths = document.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/v1/checkout/drafts", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/checkout/drafts/{orderGuid}", out _).Should().BeTrue();
        var createResponses = paths.GetProperty("/api/v1/checkout/drafts").GetProperty("post").GetProperty("responses");
        var getResponses = paths.GetProperty("/api/v1/checkout/drafts/{orderGuid}").GetProperty("get").GetProperty("responses");
        var updateResponses = paths.GetProperty("/api/v1/checkout/drafts/{orderGuid}").GetProperty("put").GetProperty("responses");
        createResponses.TryGetProperty("410", out _).Should().BeTrue();
        createResponses.TryGetProperty("409", out _).Should().BeFalse();
        getResponses.TryGetProperty("410", out _).Should().BeTrue();
        getResponses.TryGetProperty("409", out _).Should().BeFalse();
        updateResponses.TryGetProperty("409", out _).Should().BeTrue();
        updateResponses.TryGetProperty("410", out _).Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<ICheckoutDraftService>().Should().NotBeNull();
    }

    [Fact]
    public async Task PostPricingReprice_WithoutDeviceToken_ReturnsLocalizedUnauthorizedProblem()
    {
        await using var factory = new CheckoutDraftApiFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/pricing/reprice?language=he",
            CheckoutDraftTestSupport.CreateValidCreateRequest(
                factory.Snapshot,
                new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString().Should().Be(DeviceProblemCodes.TokenMissing);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
    }

    [Fact]
    public async Task PostCheckoutReprice_WithoutDeviceToken_ReturnsLocalizedUnauthorizedProblem()
    {
        await using var factory = new CheckoutDraftApiFactory();
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/reprice?language=he");
        request.Headers.TryAddWithoutValidation("X-Order-Guid", Guid.NewGuid().ToString("D"));
        request.Content = JsonContent.Create(new RepriceCheckoutDraftRequest
        {
            ExpectedVersion = 1
        });

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString().Should().Be(DeviceProblemCodes.TokenMissing);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
    }

    [Fact]
    public async Task PostPricingReprice_WhenLanguageInvalid_ReturnsLocalizedProblem()
    {
        var token = CatalogTestSupport.CreateToken(44);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/pricing/reprice?language=en",
            token,
            "he");
        request.Content = JsonContent.Create(CheckoutDraftTestSupport.CreateValidCreateRequest(
            factory.Snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        document.RootElement.GetProperty("code").GetString().Should().Be("language_invalid");
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
        document.RootElement.GetProperty("type").GetString()
            .Should().Be("https://api.ghseeli.example/errors/language_invalid");
    }

    [Fact]
    public async Task PostCheckoutReprice_WhenLanguageInvalid_ReturnsLocalizedProblem()
    {
        var token = CatalogTestSupport.CreateToken(45);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/reprice?language=en",
            token,
            "he");
        request.Headers.TryAddWithoutValidation("X-Order-Guid", Guid.NewGuid().ToString("D"));
        request.Content = JsonContent.Create(new RepriceCheckoutDraftRequest
        {
            ExpectedVersion = 1
        });

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        document.RootElement.GetProperty("code").GetString().Should().Be("language_invalid");
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
        document.RootElement.GetProperty("type").GetString()
            .Should().Be("https://api.ghseeli.example/errors/language_invalid");
    }

    [Fact]
    public async Task PostPricingReprice_WhenUnsupportedMediaType_ReturnsProblemJson()
    {
        var token = CatalogTestSupport.CreateToken(46);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/pricing/reprice",
            token,
            "he");
        request.Content = new StringContent("plain-text", Encoding.UTF8, "text/plain");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        document.RootElement.GetProperty("code").GetString()
            .Should()
            .Be(CheckoutPricingProblemCodes.UnsupportedMediaType);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
    }

    [Fact]
    public async Task PostCheckoutReprice_WhenUnsupportedMediaType_ReturnsProblemJson()
    {
        var token = CatalogTestSupport.CreateToken(47);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/reprice",
            token,
            "he");
        request.Headers.TryAddWithoutValidation("X-Order-Guid", Guid.NewGuid().ToString("D"));
        request.Content = new StringContent("plain-text", Encoding.UTF8, "text/plain");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        document.RootElement.GetProperty("code").GetString()
            .Should()
            .Be(CheckoutPricingProblemCodes.UnsupportedMediaType);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
    }

    [Fact]
    public async Task PostPricingReprice_WhenJsonIsMalformed_ReturnsBadRequestProblem()
    {
        var token = CatalogTestSupport.CreateToken(48);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/pricing/reprice",
            token,
            "he");
        request.Content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task PostCheckoutReprice_WhenJsonIsMalformed_ReturnsBadRequestProblem()
    {
        var token = CatalogTestSupport.CreateToken(49);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/reprice",
            token,
            "he");
        request.Headers.TryAddWithoutValidation("X-Order-Guid", Guid.NewGuid().ToString("D"));
        request.Content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task PostPricingReprice_WhenPayloadExceedsEndpointRequestLimit_ReturnsPayloadTooLarge()
    {
        var token = CatalogTestSupport.CreateToken(50);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/pricing/reprice?language=ar",
            token);
        request.Content = new StringContent(
            BuildOversizedCreatePayload(factory.Snapshot),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);

        await AssertPayloadTooLargeResponseAsync(response);
    }

    [Fact]
    public async Task PostCheckoutReprice_WhenPayloadExceedsEndpointRequestLimit_ReturnsPayloadTooLarge()
    {
        var token = CatalogTestSupport.CreateToken(51);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/reprice?language=ar",
            token);
        request.Headers.TryAddWithoutValidation("X-Order-Guid", Guid.NewGuid().ToString("D"));
        request.Content = new StringContent(
            BuildOversizedRepricePayload(expectedVersion: 1),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);

        await AssertPayloadTooLargeResponseAsync(response);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-REPRICE-DIRECT-068")]
    [Trait("ScenarioId", "STEP15-INVARIANT-PRICING-157")]
    public async Task PostPricingReprice_WithValidDeviceToken_ReturnsAuthoritativePricingWithoutPersistence()
    {
        var token = CatalogTestSupport.CreateToken(52);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/pricing/reprice?language=he",
            token,
            "ar");
        request.Content = JsonContent.Create(CheckoutDraftTestSupport.CreateValidCreateRequest(
            factory.Snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        document.RootElement.GetProperty("pricing").GetProperty("currency").GetString().Should().Be("ILS");
        document.RootElement.GetProperty("pricing").GetProperty("grandTotal").GetDecimal().Should().Be(79.5m);
        var methods = document.RootElement.GetProperty("paymentCapabilities")
            .GetProperty("methods")
            .EnumerateArray()
            .ToDictionary(
                method => method.GetProperty("method").GetString()!,
                method => method);
        methods["CreditCard"].GetProperty("enabled").GetBoolean().Should().BeFalse();
        methods["CreditCard"].GetProperty("reasonCode").GetString()
            .Should()
            .Be(CheckoutPaymentCapabilityReasonCodes.ProviderUnavailable);
        methods["Wallet"].GetProperty("enabled").GetBoolean().Should().BeFalse();
        methods["Wallet"].GetProperty("reasonCode").GetString()
            .Should()
            .Be(CheckoutPaymentCapabilityReasonCodes.NotYetSupported);
        methods["CashOnArrival"].GetProperty("enabled").GetBoolean().Should().BeFalse();
        methods["CashOnArrival"].GetProperty("reasonCode").GetString()
            .Should()
            .Be(CheckoutPaymentCapabilityReasonCodes.NotYetSupported);
        methods["ThirdParty"].GetProperty("enabled").GetBoolean().Should().BeFalse();
        methods["ThirdParty"].GetProperty("reasonCode").GetString()
            .Should()
            .Be(CheckoutPaymentCapabilityReasonCodes.NotYetSupported);
        (await context.CheckoutDrafts.CountAsync()).Should().Be(0);
        (await context.CheckoutDraftPricingSnapshots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PostPricingReprice_WithFixedIncludedSelection_Omitted_ReturnsNormalizedAuthoritativeSelection()
    {
        var token = CatalogTestSupport.CreateToken(53);
        var snapshot = CreateFixedIncludedSnapshot(Guid.NewGuid());
        await using var factory = new CheckoutDraftApiFactory(snapshot, devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/pricing/reprice?language=ar",
            token,
            "he");
        request.Content = JsonContent.Create(CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);
        var intentSelection = document.RootElement.GetProperty("intent")
            .GetProperty("items")[0]
            .GetProperty("selections")[0];
        var pricingSelection = document.RootElement.GetProperty("pricing")
            .GetProperty("items")[0]
            .GetProperty("selections")[0];

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        intentSelection.GetProperty("quantity").GetInt32().Should().Be(1);
        pricingSelection.GetProperty("selectionType").GetString().Should().Be("FixedIncludedChoice");
        pricingSelection.GetProperty("quantity").GetInt32().Should().Be(1);
        pricingSelection.GetProperty("isDefaultApplied").GetBoolean().Should().BeTrue();
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-REPRICE-DRAFT-069")]
    [Trait("ScenarioId", "STEP15-INVARIANT-CAPABILITIES-158")]
    public async Task PostCheckoutReprice_WithOrderGuid_PersistsSnapshotAndReturnsUpdatedDraft()
    {
        var token = CatalogTestSupport.CreateToken(54);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var orderGuid = await CreateDraftAsync(client, factory.Snapshot, token);
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/reprice?language=ar",
            token,
            "he");
        request.Headers.TryAddWithoutValidation("X-Order-Guid", orderGuid.ToString("D"));
        request.Content = JsonContent.Create(new RepriceCheckoutDraftRequest
        {
            ExpectedVersion = 1
        });

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        document.RootElement.GetProperty("version").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("requiresReprice").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("pricing").GetProperty("grandTotal").GetDecimal().Should().Be(79.5m);
        (await context.CheckoutDraftPricingSnapshots.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PostCheckoutReprice_WithStaleExpectedVersion_ReturnsStableConflict()
    {
        var token = CatalogTestSupport.CreateToken(55);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var orderGuid = await CreateDraftAsync(client, factory.Snapshot, token);
        using (var firstRepriceRequest = CreateRequest(
                   HttpMethod.Post,
                   "/api/v1/checkout/reprice?language=ar",
                   token))
        {
            firstRepriceRequest.Headers.TryAddWithoutValidation("X-Order-Guid", orderGuid.ToString("D"));
            firstRepriceRequest.Content = JsonContent.Create(new RepriceCheckoutDraftRequest
            {
                ExpectedVersion = 1
            });

            using var firstRepriceResponse = await client.SendAsync(firstRepriceRequest);
            firstRepriceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/reprice?language=he",
            token,
            "ar");
        request.Headers.TryAddWithoutValidation("X-Order-Guid", orderGuid.ToString("D"));
        request.Content = JsonContent.Create(new RepriceCheckoutDraftRequest
        {
            ExpectedVersion = 1
        });

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        document.RootElement.GetProperty("code").GetString().Should().Be(CheckoutDraftProblemCodes.VersionConflict);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
    }

    [Fact]
    public async Task PostCheckoutReprice_FromWrongDevice_ReturnsSameNotFoundProblemAsMissing()
    {
        var firstToken = CatalogTestSupport.CreateToken(56);
        var secondToken = CatalogTestSupport.CreateToken(57);
        await using var factory = new CheckoutDraftApiFactory(devices:
        [
            CatalogTestSupport.CreateDevice(firstToken, expiresAt: DateTimeOffset.UtcNow.AddDays(30)),
            CatalogTestSupport.CreateDevice(secondToken, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
        ]);
        using var client = factory.CreateApiClient();
        var orderGuid = await CreateDraftAsync(client, factory.Snapshot, firstToken);

        using var wrongDeviceRequest = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/reprice?language=he",
            secondToken);
        wrongDeviceRequest.Headers.TryAddWithoutValidation("X-Order-Guid", orderGuid.ToString("D"));
        wrongDeviceRequest.Content = JsonContent.Create(new RepriceCheckoutDraftRequest
        {
            ExpectedVersion = 1
        });
        using var wrongDeviceResponse = await client.SendAsync(wrongDeviceRequest);
        using var wrongDeviceDocument = await ReadJsonAsync(wrongDeviceResponse);

        using var missingRequest = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/reprice?language=he",
            secondToken);
        missingRequest.Headers.TryAddWithoutValidation("X-Order-Guid", Guid.NewGuid().ToString("D"));
        missingRequest.Content = JsonContent.Create(new RepriceCheckoutDraftRequest
        {
            ExpectedVersion = 1
        });
        using var missingResponse = await client.SendAsync(missingRequest);
        using var missingDocument = await ReadJsonAsync(missingResponse);

        wrongDeviceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongDeviceDocument.RootElement.GetProperty("code").GetString()
            .Should()
            .Be(missingDocument.RootElement.GetProperty("code").GetString());
        wrongDeviceDocument.RootElement.GetProperty("detail").GetString()
            .Should()
            .Be(missingDocument.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-CAPABILITIES-158")]
    public async Task PostPricingReprice_IgnoresForgedTotalsAndEnablesStripeCapabilityWhenConfigured()
    {
        var token = CatalogTestSupport.CreateToken(58);
        await using var factory = new CheckoutDraftApiFactory(
            devices:
            [
                CatalogTestSupport.CreateDevice(token, expiresAt: DateTimeOffset.UtcNow.AddDays(30))
            ],
            settings: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Stripe:PublishableKey"] = "pk_test_step11_api",
                ["Stripe:SecretKey"] = "sk_test_step11_api",
                ["Stripe:WebhookSecret"] = "whsec_step11_api"
            });
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/pricing/reprice?language=ar",
            token);
        request.Content = new StringContent(
            BuildForgedPricingPayload(factory.Snapshot),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);
        var creditCardCapability = document.RootElement.GetProperty("paymentCapabilities")
            .GetProperty("methods")
            .EnumerateArray()
            .Single(method => method.GetProperty("method").GetString() == "CreditCard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("pricing").GetProperty("currency").GetString().Should().Be("ILS");
        document.RootElement.GetProperty("pricing").GetProperty("grandTotal").GetDecimal().Should().Be(79.5m);
        creditCardCapability.GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Swagger_ContainsPricingRoutes_AndPricingServicesResolve_WithSafeDefaults()
    {
        await using var factory = new CheckoutDraftApiFactory();
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = await ReadJsonAsync(response);
        using var scope = factory.Services.CreateScope();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paths = document.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/v1/pricing/reprice", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/checkout/reprice", out _).Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<ICheckoutPricingService>().Should().NotBeNull();
    }

    private static async Task<Guid> CreateDraftAsync(
        HttpClient client,
        CatalogSnapshotResponse snapshot,
        string token)
    {
        using var request = CreateRequest(
            HttpMethod.Post,
            "/api/v1/checkout/drafts?language=ar",
            token);
        request.Content = JsonContent.Create(CheckoutDraftTestSupport.CreateValidCreateRequest(
            snapshot,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));

        using var response = await client.SendAsync(request);
        var document = await ReadJsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return document.RootElement.GetProperty("orderGuid").GetGuid();
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string? deviceToken = null,
        string? acceptLanguage = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (!string.IsNullOrWhiteSpace(deviceToken))
        {
            request.Headers.TryAddWithoutValidation(DeviceTokenDefaults.HeaderName, deviceToken);
        }

        if (!string.IsNullOrWhiteSpace(acceptLanguage))
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        request.Headers.TryAddWithoutValidation(
            InternalServiceWireConstants.CorrelationIdHeaderName,
            "corr-step10-api");
        return request;
    }

    private static string BuildOversizedCreatePayload(CatalogSnapshotResponse snapshot)
    {
        var branch = snapshot.Branches.Single();
        var offering = snapshot.Categories.Single().Offerings.Single();

        return JsonSerializer.Serialize(new
        {
            businessSourceId = snapshot.Company.Id,
            branchSourceId = branch.Id,
            requestedSlotStartUtc = "2026-08-24T10:00:00Z",
            vehicle = new
            {
                vehicleType = "Sedan",
                licensePlate = "12-345-67",
                make = "Toyota",
                model = "Corolla",
                color = "Blue"
            },
            location = new
            {
                addressLine = "الشارع الرئيسي 10",
                city = "حيفا",
                area = "الكرمل",
                latitude = 32.1,
                longitude = 34.8
            },
            items = new[]
            {
                new
                {
                    offeringSourceId = offering.Id,
                    selections = Array.Empty<object>()
                }
            },
            padding = new string('x', 70_000)
        });
    }

    private static string BuildOversizedUpdatePayload(CatalogSnapshotResponse snapshot)
    {
        var branch = snapshot.Branches.Single();
        var offering = snapshot.Categories.Single().Offerings.Single();

        return JsonSerializer.Serialize(new
        {
            expectedVersion = 1,
            businessSourceId = snapshot.Company.Id,
            branchSourceId = branch.Id,
            requestedSlotStartUtc = "2026-08-24T11:00:00Z",
            vehicle = new
            {
                vehicleType = "Sedan",
                licensePlate = "12-345-67",
                make = "Toyota",
                model = "Corolla",
                color = "Green"
            },
            location = new
            {
                addressLine = "الشارع الرئيسي 10",
                city = "حيفا",
                area = "الكرمل",
                latitude = 32.1,
                longitude = 34.8
            },
            items = new[]
            {
                new
                {
                    offeringSourceId = offering.Id,
                    selections = Array.Empty<object>()
                }
            },
            padding = new string('x', 70_000)
        });
    }

    private static string BuildOversizedRepricePayload(int expectedVersion)
    {
        return JsonSerializer.Serialize(new
        {
            expectedVersion,
            padding = new string('x', 70_000)
        });
    }

    private static string BuildForgedPricingPayload(CatalogSnapshotResponse snapshot)
    {
        var branch = snapshot.Branches.Single();
        var offering = snapshot.Categories.Single().Offerings.Single();

        return JsonSerializer.Serialize(new
        {
            businessSourceId = snapshot.Company.Id,
            branchSourceId = branch.Id,
            requestedSlotStartUtc = "2026-08-24T10:00:00Z",
            currency = "USD",
            vehicle = new
            {
                vehicleType = "Sedan",
                licensePlate = "12-345-67",
                make = "Toyota",
                model = "Corolla",
                color = "Blue"
            },
            location = new
            {
                addressLine = "الشارع الرئيسي 10",
                city = "حيفا",
                area = "الكرمل",
                latitude = 32.1,
                longitude = 34.81
            },
            items = new[]
            {
                new
                {
                    offeringSourceId = offering.Id,
                    selections = Array.Empty<object>(),
                    baseSubtotal = 0,
                    grandTotal = 1
                }
            },
            serviceFee = 999.99,
            tax = 999.99,
            grandTotal = 0
        });
    }

    private static async Task AssertPayloadTooLargeResponseAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var document = await ReadJsonAsync(response);
        document.RootElement.GetProperty("code").GetString()
            .Should()
            .Be(CheckoutPricingProblemCodes.RequestBodyTooLarge);
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
    }

    private static void AssertDraftPayloadTooLargeResponse(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType?.MediaType
            .Should()
            .BeOneOf("text/plain", "application/problem+json");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload);
    }

    private static CatalogSnapshotResponse CreateFixedIncludedSnapshot(Guid companyId)
    {
        var snapshot = CatalogTestSupport.CreateSnapshot(
            companyId,
            version: 10,
            offeringId: Guid.NewGuid(),
            addonGroupId: Guid.NewGuid(),
            addonChoiceId: Guid.NewGuid());
        var category = snapshot.Categories.Single();
        var branchId = snapshot.Branches.Single().Id;

        snapshot.Categories =
        [
            new CatalogSnapshotCategory
            {
                Id = category.Id,
                NameAr = category.NameAr,
                NameHe = category.NameHe,
                DescriptionAr = category.DescriptionAr,
                DescriptionHe = category.DescriptionHe,
                DisplayOrder = category.DisplayOrder,
                Offerings =
                [
                    new CatalogSnapshotOffering
                    {
                        Id = Guid.NewGuid(),
                        BranchId = branchId,
                        NameAr = "خيار ثابت",
                        NameHe = "בחירה קבועה",
                        DescriptionAr = "خدمة بخيار ثابت",
                        DescriptionHe = "שירות עם בחירה קבועה",
                        BasePrice = 45m,
                        DurationMinutes = 30,
                        ReferenceCode = "FIXED-01",
                        DisplayOrder = 12,
                        AddonGroups =
                        [
                            new CatalogSnapshotAddonGroup
                            {
                                Id = Guid.NewGuid(),
                                NameAr = "تضمين افتراضي",
                                NameHe = "כלול כברירת מחדל",
                                SelectionType = "FixedIncludedChoice",
                                IsRequired = true,
                                MinimumSelections = 1,
                                MaximumSelections = 1,
                                DisplayOrder = 1,
                                Choices =
                                [
                                    new CatalogSnapshotAddonChoice
                                    {
                                        Id = Guid.NewGuid(),
                                        NameAr = "مشمّل",
                                        NameHe = "כלול",
                                        PriceAdjustment = 0m,
                                        DurationAdjustmentMinutes = 0,
                                        DefaultQuantity = 1,
                                        DisplayOrder = 1
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        ];

        return snapshot;
    }
}

public sealed class CheckoutDraftApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly SqlServerCatalogDatabase _database;
    private readonly IEnumerable<CustomerDevice> _devices;
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureTestServices;

    public CheckoutDraftApiFactory(
        CatalogSnapshotResponse? snapshot = null,
        IEnumerable<CustomerDevice>? devices = null,
        DateTimeOffset? utcNow = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureTestServices = null)
    {
        _database = SqlServerCatalogDatabase.CreateAsync().GetAwaiter().GetResult();
        Snapshot = snapshot ?? CatalogTestSupport.CreateSnapshot(Guid.NewGuid(), version: 10);
        _devices = devices ?? Array.Empty<CustomerDevice>();
        TimeProvider = new ManualTimeProvider(
            utcNow ?? new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero));
        _settings = settings ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        _configureTestServices = configureTestServices;
        BusinessApiClient.GetCatalogSnapshotHandler = (_, _) => Task.FromResult(Snapshot);
        BusinessApiClient.ValidateAppointmentHandler = (request, _, _) =>
            Task.FromResult(CatalogTestSupport.CreateValidationResponse(Snapshot, request));
    }

    internal CatalogSnapshotResponse Snapshot { get; }
    internal ManualTimeProvider TimeProvider { get; }
    internal ScriptedBusinessApiClient BusinessApiClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:CustomerConnection", _database.ConnectionString);
        builder.UseSetting("JwtSettings:SecretKey", "CheckoutDraftApiTestsSecret_Minimum32Chars");
        builder.UseSetting("JwtSettings:Issuer", "GhseeliApis.CheckoutDraftTests");
        builder.UseSetting("JwtSettings:Audience", "GhseeliApis.CheckoutDraftClients");
        builder.UseSetting("Swagger:Enabled", "true");
        builder.UseSetting("CheckoutDrafts:LifetimeMinutes", "30");
        builder.UseSetting("CatalogReadModel:FreshWindowSeconds", "300");
        builder.UseSetting("CatalogReadModel:MaxStaleWindowSeconds", "3600");
        builder.UseSetting("CatalogReadModel:LeaseDurationSeconds", "30");
        builder.UseSetting("CheckoutPricing:Currency", "ILS");
        builder.UseSetting("CheckoutPricing:TaxRatePercent", "0");
        builder.UseSetting("CheckoutPricing:TaxAppliesToServiceFee", "false");
        builder.UseSetting("CheckoutPricing:ServiceFee:Mode", "None");
        builder.UseSetting("CheckoutPricing:ServiceFee:FlatAmount", "0");
        builder.UseSetting("CheckoutPricing:ServiceFee:PercentageRate", "0");
        builder.UseSetting("Stripe:PublishableKey", string.Empty);
        builder.UseSetting("Stripe:SecretKey", string.Empty);
        builder.UseSetting("Stripe:WebhookSecret", string.Empty);

        foreach (var setting in _settings)
        {
            builder.UseSetting(setting.Key, setting.Value ?? string.Empty);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<TimeProvider>();
            services.RemoveAll<IBusinessApiClient>();
            services.AddSingleton<TimeProvider>(TimeProvider);
            services.AddSingleton<IBusinessApiClient>(_ => BusinessApiClient);
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(_database.ConnectionString, sqlServerOptions =>
                {
                    sqlServerOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    sqlServerOptions.CommandTimeout(60);
                    sqlServerOptions.UseCompatibilityLevel(120);
                }));
            _configureTestServices?.Invoke(services);

            using var scope = services.BuildServiceProvider().CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (_devices.Any())
            {
                context.CustomerDevices.AddRange(_devices);
                context.SaveChanges();
            }

            CheckoutDraftTestSupport.SeedSnapshotAsync(context, Snapshot).GetAwaiter().GetResult();
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}
