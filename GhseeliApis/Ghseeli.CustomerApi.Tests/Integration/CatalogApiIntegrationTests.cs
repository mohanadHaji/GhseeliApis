using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Devices;
using GhseeliApis.Tests.Support;
using Ghseeli.IntegrationContracts.DataPartitioning;
using Ghseeli.IntegrationContracts.BusinessCatalog;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Exercises the real customer HTTP pipeline for catalog browse endpoints.
/// </summary>
public class CatalogApiIntegrationTests
{
    internal const string JwtSecret = "CatalogApiTestsSecret_Minimum32Chars";
    internal const string JwtIssuer = "GhseeliApis.CatalogTests";
    internal const string JwtAudience = "GhseeliApis.CatalogClients";

    [Fact]
    [Trait("ScenarioId", "FAN-TAXONOMY-LIST-001")]
    public async Task GetBusinessVerticals_Anonymous_ReturnsCanonicalCarWashOnce()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            "/api/v1/catalog/business-verticals?language=ar");
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var verticals = document.RootElement.GetProperty("businessVerticals");
        verticals.GetArrayLength().Should().Be(1);
        verticals[0].GetProperty("id").GetGuid()
            .Should().Be(BusinessVerticalSnapshotDefaults.CarWashId);
        verticals[0].GetProperty("code").GetString()
            .Should().Be(BusinessVerticalSnapshotDefaults.CarWashCode);
        verticals[0].GetProperty("name").GetString()
            .Should().Be(BusinessVerticalSnapshotDefaults.CarWashNameAr);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-CUSTOMER-001")]
    [Trait("ScenarioId", "FAN-AVAILABILITY-ADVISORY-018")]
    [Trait("ScenarioId", "FAN-OPTIONAL-READS-018")]
    public async Task AvailabilitySearch_Anonymous_ReturnsAdvisoryProductionResult()
    {
        await using var factory = new CatalogApiFactory();
        var branchIds = new Dictionary<Guid, Guid>
        {
            [factory.FirstSourceCompanyId] = Guid.NewGuid(),
            [factory.SecondSourceCompanyId] = Guid.NewGuid()
        };
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(
                companyId,
                branchId: branchIds[companyId]));
        factory.BusinessApiClient.DiscoverAvailabilityHandler = (request, _) =>
        {
            var candidate = request.Candidates.First();
            return Task.FromResult(new AvailabilityDiscoveryResponse
            {
                Date = request.Date,
                PreferredLocalTime = request.PreferredLocalTime,
                GeneratedAtUtc = DateTime.UtcNow,
                Results =
                [
                    new AvailabilityDiscoveryCompanyResult
                    {
                        CompanyId = candidate.CompanyId,
                        BranchId = candidate.BranchIds.First(),
                        TimeZoneId = "UTC",
                        SlotStartUtc = request.Date.ToDateTime(new TimeOnly(10, 30), DateTimeKind.Utc),
                        SlotStartLocal = request.Date.ToDateTime(new TimeOnly(10, 30)),
                        ConfiguredCapacity = 3,
                        RemainingCapacity = 2
                    }
                ]
            });
        };
        using var client = factory.CreateApiClient();
        var date = NextDay(DayOfWeek.Monday);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/catalog/businesses/availability-search?language=he",
            new AvailabilitySearchRequest
            {
                VehicleType = Ghseeli.IntegrationContracts.Vehicles.VehicleType.Sedan,
                Date = date,
                PreferredLocalTime = new TimeOnly(10, 0)
            },
            BusinessCatalogContract.CreateJsonSerializerOptions());
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("isAdvisory").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("vehicleType").GetString().Should().Be("Sedan");
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        document.RootElement.GetProperty("results").GetArrayLength().Should().Be(1);
        document.RootElement.GetProperty("results")[0]
            .GetProperty("configuredCapacity").GetInt32().Should().Be(3);
        document.RootElement.GetProperty("results")[0]
            .GetProperty("remainingCapacity").GetInt32().Should().Be(2);
        factory.BusinessApiClient.AvailabilityDiscoveryRequests.Should().Be(1);
        factory.BusinessApiClient.CreateReservationRequests.Should().Be(0);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.CustomerBookings.CountAsync()).Should().Be(0);
        (await context.CheckoutDrafts.CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-FILTER-004")]
    public async Task AvailabilitySearch_WhenLocationIsOutsideAllAreas_ReturnsEmptyWithoutUpstream()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/catalog/businesses/availability-search",
            new AvailabilitySearchRequest
            {
                VehicleType = Ghseeli.IntegrationContracts.Vehicles.VehicleType.Suv5Seater,
                Date = NextDay(DayOfWeek.Monday),
                PreferredLocalTime = new TimeOnly(10, 0),
                Latitude = -33.8688,
                Longitude = 151.2093
            },
            BusinessCatalogContract.CreateJsonSerializerOptions());
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("results").GetArrayLength().Should().Be(0);
        factory.BusinessApiClient.AvailabilityDiscoveryRequests.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-036")]
    public async Task AvailabilitySearch_WithMalformedDeviceToken_ReturnsUnauthorizedWithoutUpstream()
    {
        await using var factory = new CatalogApiFactory();
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/catalog/businesses/availability-search")
        {
            Content = JsonContent.Create(
                new AvailabilitySearchRequest
                {
                    VehicleType = Ghseeli.IntegrationContracts.Vehicles.VehicleType.Sedan,
                    Date = NextDay(DayOfWeek.Monday),
                    PreferredLocalTime = new TimeOnly(10, 0)
                },
                options: BusinessCatalogContract.CreateJsonSerializerOptions())
        };
        request.Headers.Add("X-Device-Token", "malformed");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.BusinessApiClient.AvailabilityDiscoveryRequests.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-DEMO-023")]
    [Trait("ScenarioId", "FAN-AVAILABILITY-PARTITION-007")]
    public async Task AvailabilitySearch_WithDemoDevice_UsesDemoCatalogAndDiscovery()
    {
        var token = CatalogTestSupport.CreateToken(61);
        var device = CatalogTestSupport.CreateDevice(token);
        device.IsDemo = true;
        var sourceCompanyId = Guid.Parse("61616161-6161-6161-6161-616161616161");
        var sourceBranchId = Guid.Parse("62626262-6262-6262-6262-626262626262");
        var handler = new PartitionAwareCatalogHandler(sourceCompanyId, sourceBranchId);
        await using var factory = new CatalogApiFactory(
            devices: [device],
            providers:
            [
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = sourceCompanyId,
                    Enabled = true,
                    Order = 0
                }
            ],
            useActualBusinessApiClient: true,
            useDemoProviders: true,
            businessApiHandler: handler);
        Guid productionBusinessId;
        using (var productionScope = factory.Services.CreateScope())
        {
            await CheckoutDraftTestSupport.SeedSnapshotAsync(
                productionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                CatalogTestSupport.CreateSnapshot(
                    sourceCompanyId,
                    branchId: sourceBranchId,
                    companyNameAr: "PRODUCTION-SENTINEL"));
            productionBusinessId = await productionScope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>()
                .CatalogProviders.Select(provider => provider.Id).SingleAsync();
        }
        using var client = factory.CreateApiClient();
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/catalog/businesses/availability-search")
        {
            Content = JsonContent.Create(
                new AvailabilitySearchRequest
                {
                    VehicleType = Ghseeli.IntegrationContracts.Vehicles.VehicleType.Motorcycle,
                    Date = NextDay(DayOfWeek.Monday),
                    PreferredLocalTime = new TimeOnly(11, 0)
                },
                options: BusinessCatalogContract.CreateJsonSerializerOptions())
        };
        httpRequest.Headers.Add("X-Device-Token", token);

        using var response = await client.SendAsync(httpRequest);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = document.RootElement.GetProperty("results");
        result.GetArrayLength().Should().Be(1);
        var demoBusinessId = result[0].GetProperty("businessId").GetGuid();
        var demoBranchId = result[0].GetProperty("branchId").GetGuid();
        using (var demoScope = factory.Services.CreateScope())
        {
            demoScope.ServiceProvider
                .GetRequiredService<GhseeliApis.DataPartitioning.ICustomerDataPartitionContext>()
                .SetTrustedPartition(DataPartitionNames.Demo);
            var demoProvider = await demoScope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>()
                .CatalogProviders.Include(provider => provider.Branches)
                .SingleAsync(provider => provider.Id == demoBusinessId);
            demoProvider.IsDemo.Should().BeTrue();
            demoProvider.Branches.Should().ContainSingle(branch => branch.Id == demoBranchId);
        }
        document.RootElement.GetRawText().Should().NotContain(productionBusinessId.ToString());
        handler.RequestedPaths.Count(path => path.EndsWith(
            "/appointments/availability-discovery",
            StringComparison.Ordinal)).Should().Be(1);
        handler.RequestedPaths.Should().Contain(
            "/api/v1/internal/appointments/availability-discovery");
        handler.RequestedPartitions.Should().OnlyContain(
            partition => partition == DataPartitionNames.Demo);
        handler.AvailabilityRequests.Should().ContainSingle();
        handler.AvailabilityRequests.Single().Candidates.Should().ContainSingle()
            .Which.CompanyId.Should().Be(sourceCompanyId);
        handler.AvailabilityRequests.Single().Candidates.Single().BranchIds
            .Should().Equal(sourceBranchId);
        handler.AvailabilitySignatureHeaders.Should().ContainSingle()
            .Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-ADVISORY-032")]
    public async Task AvailabilityDiscovery_ResultBranch_RemainsSubjectToOfferingAwareDetailedSlotsValidation()
    {
        var token = CatalogTestSupport.CreateToken(64);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)]);
        var sourceBranchId = Guid.Parse("32323232-3232-3232-3232-323232323232");
        var sourceOfferingId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 44,
                branchId: sourceBranchId,
                offeringId: sourceOfferingId));
        factory.BusinessApiClient.DiscoverAvailabilityHandler = (request, _) =>
            Task.FromResult(new AvailabilityDiscoveryResponse
            {
                ContractVersion = BusinessCatalogContract.Version,
                Date = request.Date,
                PreferredLocalTime = request.PreferredLocalTime,
                GeneratedAtUtc = DateTime.UtcNow,
                Results =
                [
                    new AvailabilityDiscoveryCompanyResult
                    {
                        CompanyId = request.Candidates.First().CompanyId,
                        BranchId = request.Candidates.First().BranchIds.First(),
                        TimeZoneId = "UTC",
                        SlotStartUtc = request.Date.ToDateTime(
                            new TimeOnly(11), DateTimeKind.Utc),
                        SlotStartLocal = request.Date.ToDateTime(new TimeOnly(11)),
                        ConfiguredCapacity = 9,
                        RemainingCapacity = 8
                    }
                ]
            });
        factory.BusinessApiClient.GetAvailableSlotsHandler = (request, _) =>
            Task.FromResult(new AvailableSlotsResponse
            {
                Valid = true,
                CompanyId = request.CompanyId,
                BranchId = request.BranchId,
                Date = request.Date,
                TimeZoneId = "UTC",
                CatalogVersion = request.ExpectedCatalogVersion!.Value,
                Currency = request.Currency,
                TotalDurationMinutes = 30,
                GeneratedAtUtc = DateTime.UtcNow,
                Slots =
                [
                    new AvailableSlotResponse
                    {
                        StartUtc = request.Date.ToDateTime(
                            new TimeOnly(14), DateTimeKind.Utc),
                        EndUtc = request.Date.ToDateTime(
                            new TimeOnly(14, 30), DateTimeKind.Utc),
                        StartLocal = request.Date.ToDateTime(new TimeOnly(14)),
                        EndLocal = request.Date.ToDateTime(new TimeOnly(14, 30)),
                        ConfiguredCapacity = 4,
                        RemainingCapacity = 1,
                        IsAvailable = true
                    }
                ]
            });
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-Device-Token", token);
        var date = NextDay(DayOfWeek.Monday);

        using var discovery = await client.PostAsJsonAsync(
            "/api/v1/catalog/businesses/availability-search",
            new AvailabilitySearchRequest
            {
                VehicleType = Ghseeli.IntegrationContracts.Vehicles.VehicleType.Sedan,
                Date = date,
                PreferredLocalTime = new TimeOnly(11)
            },
            BusinessCatalogContract.CreateJsonSerializerOptions());
        using var discoveryJson = await ReadJsonAsync(discovery);
        var discovered = discoveryJson.RootElement.GetProperty("results")[0];
        var businessId = discovered.GetProperty("businessId").GetGuid();
        var branchId = discovered.GetProperty("branchId").GetGuid();
        using var offerings = await client.GetAsync(
            $"/api/v1/catalog/businesses/{businessId:D}/offerings?branchId={branchId:D}");
        using var offeringsJson = await ReadJsonAsync(offerings);
        var offeringId = offeringsJson.RootElement.GetProperty("offerings")[0]
            .GetProperty("id").GetGuid();
        using var detailed = await client.PostAsJsonAsync(
            $"/api/v1/catalog/businesses/{businessId:D}/branches/{branchId:D}/available-slots",
            new
            {
                date,
                items = new[] { new { offeringId } }
            });
        using var detailedJson = await ReadJsonAsync(detailed);

        discovery.StatusCode.Should().Be(HttpStatusCode.OK);
        detailed.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.BusinessApiClient.AvailabilityDiscoveryRequests.Should().Be(1);
        factory.BusinessApiClient.AvailableSlotsRequests.Should().Be(1);
        factory.BusinessApiClient.CreateReservationRequests.Should().Be(0);
        var authoritative = factory.BusinessApiClient.AvailableSlotsRequestsLog.Single();
        authoritative.CompanyId.Should().Be(factory.FirstSourceCompanyId);
        authoritative.BranchId.Should().Be(sourceBranchId);
        authoritative.ExpectedCatalogVersion.Should().Be(44);
        authoritative.Currency.Should().Be("ILS");
        authoritative.Items.Should().ContainSingle()
            .Which.OfferingId.Should().Be(sourceOfferingId);
        detailedJson.RootElement.GetProperty("slots")[0]
            .GetProperty("remainingCapacity").GetInt32().Should().Be(1);
        detailedJson.RootElement.GetProperty("slots")[0]
            .GetProperty("configuredCapacity").GetInt32().Should().Be(4);
        discovered.GetProperty("configuredCapacity").GetInt32().Should().Be(9);
        discovered.GetProperty("remainingCapacity").GetInt32().Should().Be(8);
    }

    [Fact]
    public async Task AvailabilitySearch_WithCustomerJwtAndNoDevice_UsesJwtPartition()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        factory.BusinessApiClient.DiscoverAvailabilityHandler = (request, _) =>
            Task.FromResult(new AvailabilityDiscoveryResponse
            {
                Date = request.Date,
                PreferredLocalTime = request.PreferredLocalTime,
                GeneratedAtUtc = DateTime.UtcNow
            });
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid(), partition: DataPartitionNames.Production));

        using var response = await client.PostAsJsonAsync(
            "/api/v1/catalog/businesses/availability-search",
            new AvailabilitySearchRequest
            {
                VehicleType = Ghseeli.IntegrationContracts.Vehicles.VehicleType.Sedan,
                Date = NextDay(DayOfWeek.Monday),
                PreferredLocalTime = new TimeOnly(10)
            },
            BusinessCatalogContract.CreateJsonSerializerOptions());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.BusinessApiClient.AvailabilityDiscoveryRequests.Should().Be(1);
    }

    [Fact]
    public async Task AvailabilitySearch_WithMatchingJwtAndDevice_ProcessesOnce()
    {
        var token = CatalogTestSupport.CreateToken(62);
        var device = CatalogTestSupport.CreateDevice(token);
        await using var factory = new CatalogApiFactory(devices: [device]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        factory.BusinessApiClient.DiscoverAvailabilityHandler = (request, _) =>
            Task.FromResult(new AvailabilityDiscoveryResponse
            {
                Date = request.Date,
                PreferredLocalTime = request.PreferredLocalTime,
                GeneratedAtUtc = DateTime.UtcNow
            });
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/catalog/businesses/availability-search")
        {
            Content = JsonContent.Create(
                new AvailabilitySearchRequest
                {
                    VehicleType = Ghseeli.IntegrationContracts.Vehicles.VehicleType.Sedan,
                    Date = NextDay(DayOfWeek.Monday),
                    PreferredLocalTime = new TimeOnly(10)
                },
                options: BusinessCatalogContract.CreateJsonSerializerOptions())
        };
        request.Headers.Add("X-Device-Token", token);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid(), partition: DataPartitionNames.Production));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.BusinessApiClient.AvailabilityDiscoveryRequests.Should().Be(1);
    }

    [Fact]
    public async Task AvailabilitySearch_WithMismatchedJwtAndDevice_IsForbiddenWithoutUpstream()
    {
        var token = CatalogTestSupport.CreateToken(63);
        var device = CatalogTestSupport.CreateDevice(token);
        device.IsDemo = true;
        await using var factory = new CatalogApiFactory(
            devices: [device],
            useDemoProviders: true);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/catalog/businesses/availability-search")
        {
            Content = JsonContent.Create(
                new AvailabilitySearchRequest
                {
                    VehicleType = Ghseeli.IntegrationContracts.Vehicles.VehicleType.Sedan,
                    Date = NextDay(DayOfWeek.Monday),
                    PreferredLocalTime = new TimeOnly(10)
                },
                options: BusinessCatalogContract.CreateJsonSerializerOptions())
        };
        request.Headers.Add("X-Device-Token", token);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid(), partition: DataPartitionNames.Production));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.BusinessApiClient.AvailabilityDiscoveryRequests.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-VALIDATION-005")]
    public Task AvailabilitySearch_InvalidVehicleType_ReturnsFieldErrorWithoutDiscovery() =>
        AssertAvailabilityValidationAsync(
            "vehicle",
            CatalogProblemCodes.VehicleTypeInvalid,
            "vehicleType");

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-VALIDATION-020")]
    public Task AvailabilitySearch_PastDate_ReturnsFieldErrorWithoutDiscovery() =>
        AssertAvailabilityValidationAsync(
            "past-date",
            CatalogProblemCodes.DateInvalid,
            "date");

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-VALIDATION-021")]
    public Task AvailabilitySearch_InvalidTime_ReturnsFieldErrorWithoutDiscovery() =>
        AssertAvailabilityValidationAsync(
            "time",
            CatalogProblemCodes.PreferredLocalTimeInvalid,
            "preferredLocalTime");

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-VALIDATION-022")]
    public Task AvailabilitySearch_InvalidCategory_ReturnsFieldErrorWithoutDiscovery() =>
        AssertAvailabilityValidationAsync(
            "category",
            CatalogProblemCodes.CategoryInvalid,
            "categoryId");

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-VALIDATION-023")]
    public Task AvailabilitySearch_PartialLocation_ReturnsFieldErrorWithoutDiscovery() =>
        AssertAvailabilityValidationAsync(
            "partial-location",
            CatalogProblemCodes.LocationInvalid,
            "location");

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-VALIDATION-024")]
    public Task AvailabilitySearch_OutOfRangeLocation_ReturnsFieldErrorsWithoutDiscovery() =>
        AssertAvailabilityValidationAsync(
            "location-range",
            CatalogProblemCodes.LocationInvalid,
            "latitude",
            "longitude");

    private static async Task AssertAvailabilityValidationAsync(
        string condition,
        string expectedCode,
        params string[] expectedFields)
    {
        await using var factory = new CatalogApiFactory();
        using var client = factory.CreateApiClient();
        var future = NextDay(DayOfWeek.Monday).ToString("yyyy-MM-dd");
        var past = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd");
        var json = condition switch
        {
            "vehicle" =>
                $$"""{"vehicleType":"Car","date":"{{future}}","preferredLocalTime":"10:00:00"}""",
            "past-date" =>
                $$"""{"vehicleType":"Sedan","date":"{{past}}","preferredLocalTime":"10:00:00"}""",
            "time" =>
                $$"""{"vehicleType":"Sedan","date":"{{future}}","preferredLocalTime":"13:30"}""",
            "category" =>
                $$"""{"vehicleType":"Sedan","date":"{{future}}","preferredLocalTime":"10:00:00","categoryId":"invalid"}""",
            "partial-location" =>
                $$"""{"vehicleType":"Sedan","date":"{{future}}","preferredLocalTime":"10:00:00","latitude":31.7}""",
            _ =>
                $$"""{"vehicleType":"Sedan","date":"{{future}}","preferredLocalTime":"10:00:00","latitude":91,"longitude":181}"""
        };

        using var response = await client.PostAsync(
            "/api/v1/catalog/businesses/availability-search",
            new StringContent(json, Encoding.UTF8, "application/json"));
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString().Should().Be(expectedCode);
        document.RootElement.GetProperty("fieldErrors").EnumerateObject()
            .Select(property => property.Name)
            .Should().Contain(expectedFields);
        factory.BusinessApiClient.AvailabilityDiscoveryRequests.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-FAILURE-008")]
    public Task AvailabilitySearch_Timeout_ReturnsExactUnavailableWithoutResults() =>
        AssertAvailabilityFailureAsync("timeout", HttpStatusCode.ServiceUnavailable);

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-FAILURE-025")]
    public Task AvailabilitySearch_ServerError_ReturnsExactUnavailableWithoutResults() =>
        AssertAvailabilityFailureAsync("server-error", HttpStatusCode.ServiceUnavailable);

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-FAILURE-026")]
    public Task AvailabilitySearch_MalformedSuccess_ReturnsExactBadGatewayWithoutResults() =>
        AssertAvailabilityFailureAsync("invalid-success", HttpStatusCode.BadGateway);

    [Fact]
    [Trait("ScenarioId", "FAN-AVAILABILITY-FAILURE-027")]
    public async Task AvailabilitySearch_OversizedBusinessSuccess_ReturnsBadGatewayWithoutResults()
    {
        var handler = new PartitionAwareCatalogHandler.OversizedAvailabilityHandler();
        await using var factory = new CatalogApiFactory(
            providers:
            [
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = handler.SourceCompanyId,
                    Enabled = true,
                    Order = 0
                }
            ],
            useActualBusinessApiClient: true,
            businessApiHandler: handler);
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/catalog/businesses/availability-search",
            new AvailabilitySearchRequest
            {
                VehicleType = Ghseeli.IntegrationContracts.Vehicles.VehicleType.Sedan,
                Date = NextDay(DayOfWeek.Monday),
                PreferredLocalTime = new TimeOnly(10)
            },
            BusinessCatalogContract.CreateJsonSerializerOptions());
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(CatalogProblemCodes.AvailabilityUnavailable);
        document.RootElement.TryGetProperty("results", out _).Should().BeFalse();
        handler.AvailabilityRequests.Should().Be(1);
    }

    private static async Task AssertAvailabilityFailureAsync(
        string failure,
        HttpStatusCode expectedStatus)
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        factory.BusinessApiClient.DiscoverAvailabilityHandler = (_, _) =>
            Task.FromException<AvailabilityDiscoveryResponse>(failure switch
            {
                "timeout" => new TaskCanceledException("timed out"),
                "server-error" => new BusinessApiUnavailableException(
                    "Business API returned 500.", "corr"),
                _ => new BusinessApiContractException(
                    "Invalid success payload.", "corr")
            });
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/catalog/businesses/availability-search",
            new AvailabilitySearchRequest
            {
                VehicleType = Ghseeli.IntegrationContracts.Vehicles.VehicleType.Sedan,
                Date = NextDay(DayOfWeek.Monday),
                PreferredLocalTime = new TimeOnly(10)
            },
            BusinessCatalogContract.CreateJsonSerializerOptions());
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType!.MediaType.Should()
            .Be("application/problem+json");
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(CatalogProblemCodes.AvailabilityUnavailable);
        document.RootElement.TryGetProperty("results", out _).Should().BeFalse();
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BUSINESS-BROWSE-001")]
    [Trait("ScenarioId", "FAN-OPTIONAL-CATALOG-013")]
    public async Task GetBusinesses_WithoutDeviceToken_ReturnsAnonymousProductionProjection()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/api/v1/catalog/businesses?language=he");
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.BusinessApiClient.AvailableSlotsRequests.Should().Be(0);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        document.RootElement.GetProperty("businesses").EnumerateArray()
            .Should().OnlyContain(business =>
                !business.GetProperty("isFavourite").GetBoolean() &&
                business.GetProperty("averageRating").GetDecimal() == 0m &&
                business.GetProperty("ratingCount").GetInt32() == 0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-CATALOG-002")]
    public Task GetCategories_WithoutToken_ReturnsProductionWithoutDemoLeakage() =>
        AssertAnonymousProductionCatalogRouteAsync(AnonymousCatalogRoute.Categories);

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-CATALOG-014")]
    public Task GetBusinessDetail_WithoutToken_ReturnsProductionWithoutDemoLeakage() =>
        AssertAnonymousProductionCatalogRouteAsync(AnonymousCatalogRoute.BusinessDetail);

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-CATALOG-015")]
    public Task GetBusinessOfferings_WithoutToken_ReturnsProductionWithoutDemoLeakage() =>
        AssertAnonymousProductionCatalogRouteAsync(AnonymousCatalogRoute.BusinessOfferings);

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-CATALOG-016")]
    public Task GetOfferingDetail_WithoutToken_ReturnsProductionWithoutDemoLeakage() =>
        AssertAnonymousProductionCatalogRouteAsync(AnonymousCatalogRoute.OfferingDetail);

    [Fact]
    public async Task GetBusinesses_WithMalformedDeviceToken_ReturnsArabicUnauthorizedProblem()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            deviceToken: "short-token",
            acceptLanguage: "-, ;q=1");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString().Should().Be(DeviceProblemCodes.TokenInvalid);
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-DEV-DEMO-CATALOG-002")]
    public async Task GetBusinesses_DemoOnlyEnvironment_IgnoresCredentialsAndReturnsDemo()
    {
        await using var factory = new CatalogApiFactory(
            useDemoProviders: true,
            demoPublicApisOnly: true);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            "malformed");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid(), "User", DataPartitionNames.Production));

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("businesses").GetArrayLength()
            .Should().Be(2);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-028")]
    public Task Categories_RejectMalformedSuppliedDevice() =>
        AssertCatalogRouteRejectsMalformedDeviceAsync("/api/v1/catalog/categories");

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-029")]
    public Task Businesses_RejectMalformedSuppliedDevice() =>
        AssertCatalogRouteRejectsMalformedDeviceAsync("/api/v1/catalog/businesses");

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-030")]
    public Task BusinessDetail_RejectsMalformedSuppliedDevice() =>
        AssertCatalogRouteRejectsMalformedDeviceAsync(
            "/api/v1/catalog/businesses/00000000-0000-0000-0000-000000000001");

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-031")]
    public Task BusinessOfferings_RejectMalformedSuppliedDevice() =>
        AssertCatalogRouteRejectsMalformedDeviceAsync(
            "/api/v1/catalog/businesses/00000000-0000-0000-0000-000000000001/offerings");

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-INVALID-032")]
    public Task OfferingDetail_RejectsMalformedSuppliedDevice() =>
        AssertCatalogRouteRejectsMalformedDeviceAsync(
            "/api/v1/catalog/offerings/00000000-0000-0000-0000-000000000001");

    private static async Task AssertCatalogRouteRejectsMalformedDeviceAsync(string path)
    {
        await using var factory = new CatalogApiFactory();
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(HttpMethod.Get, path, "malformed");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(DeviceProblemCodes.TokenInvalid);
        factory.BusinessApiClient.AvailableSlotsRequests.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BUSINESS-SEARCH-002")]
    [Trait("ScenarioId", "FAN-BUSINESS-SEARCH-003")]
    public async Task GetBusinesses_SearchMatchesArabicAndHebrewIndependentOfDisplayLanguage()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(companyId == factory.FirstSourceCompanyId
                ? CatalogTestSupport.CreateSnapshot(
                    companyId, companyNameAr: "شركة اللمعة", companyNameHe: "ברק")
                : CatalogTestSupport.CreateSnapshot(
                    companyId, companyNameAr: "شركة أخرى", companyNameHe: "אחר"));
        using var client = factory.CreateApiClient();

        using var arabic = await client.GetAsync(
            "/api/v1/catalog/businesses?search=%20اللمعة%20&language=ar");
        using var arabicJson = await ReadJsonAsync(arabic);
        using var hebrew = await client.GetAsync(
            "/api/v1/catalog/businesses?search=ברק&language=ar");
        using var hebrewJson = await ReadJsonAsync(hebrew);

        arabic.StatusCode.Should().Be(HttpStatusCode.OK);
        hebrew.StatusCode.Should().Be(HttpStatusCode.OK);
        arabicJson.RootElement.GetProperty("businesses").GetArrayLength().Should().Be(1);
        hebrewJson.RootElement.GetProperty("businesses").GetArrayLength().Should().Be(1);
        arabicJson.RootElement.GetProperty("businesses")[0].GetProperty("sourceId")
            .GetGuid().Should().Be(factory.FirstSourceCompanyId);
        hebrewJson.RootElement.GetProperty("businesses")[0].GetProperty("sourceId")
            .GetGuid().Should().Be(factory.FirstSourceCompanyId);
    }

    [Fact]
    public async Task GetBusinesses_SearchUsesFormCAndHandlesEmptyAndNoMatch()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(companyId == factory.FirstSourceCompanyId
                ? CatalogTestSupport.CreateSnapshot(
                    companyId, companyNameAr: "مقهى Café", companyNameHe: "קפה")
                : CatalogTestSupport.CreateSnapshot(
                    companyId, companyNameAr: "شركة أخرى", companyNameHe: "אחר"));
        using var client = factory.CreateApiClient();

        using var composed = await client.GetAsync(
            "/api/v1/catalog/businesses?search=Cafe%CC%81");
        using var composedJson = await ReadJsonAsync(composed);
        using var empty = await client.GetAsync(
            "/api/v1/catalog/businesses?search=%E2%80%8B");
        using var emptyJson = await ReadJsonAsync(empty);
        using var noMatch = await client.GetAsync(
            "/api/v1/catalog/businesses?search=does-not-exist");
        using var noMatchJson = await ReadJsonAsync(noMatch);

        composed.StatusCode.Should().Be(HttpStatusCode.OK);
        composedJson.RootElement.GetProperty("businesses").GetArrayLength().Should().Be(1);
        composedJson.RootElement.GetProperty("businesses")[0].GetProperty("sourceId")
            .GetGuid().Should().Be(factory.FirstSourceCompanyId);
        empty.StatusCode.Should().Be(HttpStatusCode.OK);
        emptyJson.RootElement.GetProperty("businesses").GetArrayLength().Should().Be(2);
        noMatch.StatusCode.Should().Be(HttpStatusCode.OK);
        noMatchJson.RootElement.GetProperty("businesses").GetArrayLength().Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BUSINESS-TOP-020")]
    public async Task GetBusinesses_InvalidTop_ReturnsStableProblemCode()
    {
        await using var factory = new CatalogApiFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/api/v1/catalog/businesses?top=text");
        using var json = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.RootElement.GetProperty("code").GetString()
            .Should().Be(CatalogProblemCodes.TopInvalid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(11)]
    public async Task GetBusinesses_UnsupportedNumericTop_ReturnsStableProblemCode(int top)
    {
        await using var factory = new CatalogApiFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync($"/api/v1/catalog/businesses?top={top}");
        using var json = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        json.RootElement.GetProperty("code").GetString()
            .Should().Be(CatalogProblemCodes.TopInvalid);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-008")]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-021")]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-009")]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-022")]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-023")]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-010")]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-024")]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-025")]
    public async Task Favourite_PutGetDelete_AreAuthenticatedAndIdempotent()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "favourite@example.test",
            Email = "favourite@example.test",
            FullName = "Favourite User"
        };
        await using var factory = new CatalogApiFactory(users: [user]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();
        using var browse = await client.GetAsync("/api/v1/catalog/businesses");
        using var browseJson = await ReadJsonAsync(browse);
        var businessId = browseJson.RootElement.GetProperty("businesses")[0]
            .GetProperty("id").GetGuid();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(user.Id));

        using (var put = await client.PutAsync(
                   $"/api/v1/catalog/businesses/{businessId:D}/favourite", null))
        {
            put.StatusCode.Should().Be(HttpStatusCode.NoContent);
            put.Headers.CacheControl!.NoStore.Should().BeTrue();
        }
        (await client.PutAsync(
            $"/api/v1/catalog/businesses/{businessId:D}/favourite", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        using (var verifyScope = factory.Services.CreateScope())
        {
            (await verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .BusinessFavourites.CountAsync()).Should().Be(1);
        }
        using var authenticatedBrowse = await client.GetAsync("/api/v1/catalog/businesses");
        using var authenticatedJson = await ReadJsonAsync(authenticatedBrowse);
        authenticatedJson.RootElement.GetProperty("businesses")[0]
            .GetProperty("isFavourite").GetBoolean().Should().BeTrue();
        using var detail = await client.GetAsync(
            $"/api/v1/catalog/businesses/{businessId:D}");
        using var detailJson = await ReadJsonAsync(detail);
        detailJson.RootElement.GetProperty("business").GetProperty("isFavourite")
            .GetBoolean().Should().BeTrue();
        using var offerings = await client.GetAsync(
            $"/api/v1/catalog/businesses/{businessId:D}/offerings");
        using var offeringsJson = await ReadJsonAsync(offerings);
        offeringsJson.RootElement.GetProperty("business").GetProperty("isFavourite")
            .GetBoolean().Should().BeTrue();

        (await client.DeleteAsync(
            $"/api/v1/catalog/businesses/{businessId:D}/favourite"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync(
            $"/api/v1/catalog/businesses/{businessId:D}/favourite"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var afterDelete = await client.GetAsync(
            $"/api/v1/catalog/businesses/{businessId:D}");
        using var afterDeleteJson = await ReadJsonAsync(afterDelete);
        afterDeleteJson.RootElement.GetProperty("business").GetProperty("isFavourite")
            .GetBoolean().Should().BeFalse();
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .BusinessFavourites.CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-011")]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-012")]
    public async Task Favourite_RequiresCustomerJwt_AndUnknownBusinessIsNonDisclosing()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "unknown-favourite@example.test",
            Email = "unknown-favourite@example.test",
            FullName = "Unknown Favourite User"
        };
        await using var factory = new CatalogApiFactory(users: [user]);
        using var client = factory.CreateApiClient();
        var path = $"/api/v1/catalog/businesses/{Guid.NewGuid():D}/favourite";

        using var anonymous = await client.PutAsync(path, null);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(user.Id));
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "favourite-safe-correlation");
        using var unknown = await client.SendAsync(request);
        using var unknownJson = await ReadJsonAsync(unknown);

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        unknown.Headers.CacheControl!.NoStore.Should().BeTrue();
        unknown.Headers.GetValues("X-Correlation-Id")
            .Should().Equal("favourite-safe-correlation");
        unknownJson.RootElement.GetProperty("code").GetString()
            .Should().Be(CatalogProblemCodes.BusinessNotFound);
        var payload = unknownJson.RootElement.GetRawText();
        payload.Should().NotContain(user.Email);
        payload.ToLowerInvariant().Should().NotContain("select");
        payload.ToLowerInvariant().Should().NotContain("businessfavourites");
    }

    [Fact]
    public async Task Favourite_MalformedAndNonUserJwt_DoNotMutate()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "rejected-favourite@example.test",
            Email = "rejected-favourite@example.test",
            FullName = "Rejected Favourite User"
        };
        await using var factory = new CatalogApiFactory(users: [user]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();
        using var browse = await client.GetAsync("/api/v1/catalog/businesses");
        using var browseJson = await ReadJsonAsync(browse);
        var businessId = browseJson.RootElement.GetProperty("businesses")[0]
            .GetProperty("id").GetGuid();
        var path = $"/api/v1/catalog/businesses/{businessId:D}/favourite";

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        using var malformed = await client.PutAsync(path, null);
        malformed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateJwt(user.Id, role: "Admin"));
        using var admin = await client.PutAsync(path, null);
        admin.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .BusinessFavourites.CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-JWT-008")]
    public async Task Favourite_DemoJwtWithoutDevice_UsesDemoPartitionAndIdentity()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "demo-favourite@example.test",
            Email = "demo-favourite@example.test",
            FullName = "Demo Favourite User"
        };
        await using var factory = new CatalogApiFactory(
            users: [user],
            useDemoProviders: true);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(user.Id, partition: DataPartitionNames.Demo));

        using var browse = await client.GetAsync("/api/v1/catalog/businesses");
        using var browseJson = await ReadJsonAsync(browse);
        var business = browseJson.RootElement.GetProperty("businesses")[0];
        var businessId = business.GetProperty("id").GetGuid();
        var sourceId = business.GetProperty("sourceId").GetGuid();
        using var put = await client.PutAsync(
            $"/api/v1/catalog/businesses/{businessId:D}/favourite",
            null);

        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider
            .GetRequiredService<GhseeliApis.DataPartitioning.ICustomerDataPartitionContext>()
            .SetTrustedPartition(DataPartitionNames.Demo);
        var favourite = await scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>()
            .BusinessFavourites.SingleAsync();
        favourite.UserId.Should().Be(user.Id);
        favourite.BusinessSourceId.Should().Be(sourceId);
        favourite.IsDemo.Should().BeTrue();
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-MISMATCH-009")]
    public async Task GetBusinesses_WithMismatchedJwtAndOptionalDevice_IsForbidden()
    {
        var demoToken = CatalogTestSupport.CreateToken(29);
        var demoDevice = CatalogTestSupport.CreateDevice(demoToken);
        demoDevice.IsDemo = true;
        await using var factory = new CatalogApiFactory(
            devices: [demoDevice],
            useDemoProviders: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            demoToken);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid(), partition: DataPartitionNames.Production));

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be("data_partition_mismatch");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-033")]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-034")]
    public async Task Favourite_PutAndDelete_RequireMatchingJwtAndDevicePartitions()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "partition-favourite@example.test",
            Email = "partition-favourite@example.test",
            FullName = "Partition Favourite User"
        };
        var productionToken = CatalogTestSupport.CreateToken(48);
        var demoToken = CatalogTestSupport.CreateToken(49);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(productionToken)],
            users: [user]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();
        using var browse = await client.GetAsync("/api/v1/catalog/businesses");
        using var browseJson = await ReadJsonAsync(browse);
        var businessId = browseJson.RootElement.GetProperty("businesses")[0]
            .GetProperty("id").GetGuid();
        var path = $"/api/v1/catalog/businesses/{businessId:D}/favourite";
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(user.Id));

        using (var put = await client.SendAsync(CreateRequest(
                   HttpMethod.Put, path, productionToken)))
        {
            put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        using (var delete = await client.SendAsync(CreateRequest(
                   HttpMethod.Delete, path, productionToken)))
        {
            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var demoScope = factory.Services.CreateScope())
        {
            demoScope.ServiceProvider
                .GetRequiredService<GhseeliApis.DataPartitioning.ICustomerDataPartitionContext>()
                .SetTrustedPartition(DataPartitionNames.Demo);
            var context = demoScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.CustomerDevices.Add(CatalogTestSupport.CreateDevice(demoToken));
            await context.SaveChangesAsync();
        }

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
        {
            using var request = CreateRequest(method, path, demoToken);
            request.Headers.TryAddWithoutValidation(
                "X-Correlation-Id", $"partition-{method.Method.ToLowerInvariant()}");
            using var response = await client.SendAsync(request);
            using var document = await ReadJsonAsync(response);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            response.Content.Headers.ContentType!.MediaType.Should()
                .Be("application/problem+json");
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            response.Headers.GetValues("X-Correlation-Id").Should()
                .Equal($"partition-{method.Method.ToLowerInvariant()}");
            document.RootElement.GetProperty("code").GetString()
                .Should().Be("data_partition_mismatch");
        }

        using var verifyScope = factory.Services.CreateScope();
        (await verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .BusinessFavourites.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-035")]
    public async Task Favourite_PutAndDelete_UnknownDisabledAndCrossPartitionTargetsHaveExactParity()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "parity-favourite@example.test",
            Email = "parity-favourite@example.test",
            FullName = "Parity Favourite User"
        };
        var disabled = CreateHiddenProvider(isEnabled: false);
        var demoOnly = CreateHiddenProvider(isEnabled: true);
        await using var factory = new CatalogApiFactory(
            catalogProviders: [disabled],
            users: [user]);
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(user.Id));

        using (var demoScope = factory.Services.CreateScope())
        {
            demoScope.ServiceProvider
                .GetRequiredService<GhseeliApis.DataPartitioning.ICustomerDataPartitionContext>()
                .SetTrustedPartition(DataPartitionNames.Demo);
            var context = demoScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.CatalogProviders.Add(demoOnly);
            await context.SaveChangesAsync();
        }

        var targets = new[] { Guid.NewGuid(), disabled.Id, demoOnly.Id };
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
        {
            string? canonicalBody = null;
            foreach (var target in targets)
            {
                using var request = new HttpRequestMessage(
                    method,
                    $"/api/v1/catalog/businesses/{target:D}/favourite");
                request.Headers.TryAddWithoutValidation(
                    "X-Correlation-Id", "favourite-parity-correlation");
                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(body);

                response.StatusCode.Should().Be(HttpStatusCode.NotFound);
                response.Content.Headers.ContentType!.MediaType.Should()
                    .Be("application/problem+json");
                response.Headers.CacheControl!.NoStore.Should().BeTrue();
                response.Headers.GetValues("X-Correlation-Id")
                    .Should().Equal("favourite-parity-correlation");
                document.RootElement.GetProperty("code").GetString()
                    .Should().Be(CatalogProblemCodes.BusinessNotFound);
                document.RootElement.EnumerateObject().Select(value => value.Name)
                    .Should().BeEquivalentTo(
                        "type", "title", "status", "detail", "code", "correlationId",
                        "language");
                body.ToLowerInvariant().Should().NotContain(target.ToString().ToLowerInvariant());
                body.ToLowerInvariant().Should().NotContain(user.Email!.ToLowerInvariant());
                canonicalBody ??= body;
                body.Should().Be(canonicalBody);
            }
        }

        using var verifyScope = factory.Services.CreateScope();
        (await verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .BusinessFavourites.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-036")]
    [Trait("ScenarioId", "FAN-BUSINESS-FAVOURITE-037")]
    public async Task Favourite_DeleteSuccessAndPutDeleteAuthorizationResponses_AreSafeAndStable()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "delete-favourite@example.test",
            Email = "delete-favourite@example.test",
            FullName = "Delete Favourite User"
        };
        await using var factory = new CatalogApiFactory(users: [user]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();
        using var browse = await client.GetAsync("/api/v1/catalog/businesses");
        using var browseJson = await ReadJsonAsync(browse);
        var businessId = browseJson.RootElement.GetProperty("businesses")[0]
            .GetProperty("id").GetGuid();
        var path = $"/api/v1/catalog/businesses/{businessId:D}/favourite";
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(user.Id));
        using (var put = await client.PutAsync(path, null))
        {
            put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var request = new HttpRequestMessage(HttpMethod.Delete, path))
        {
            request.Headers.TryAddWithoutValidation(
                "X-Correlation-Id", "favourite-delete-success");
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            response.Headers.GetValues("X-Correlation-Id")
                .Should().Equal("favourite-delete-success");
            (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        }

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
        {
            client.DefaultRequestHeaders.Authorization = null;
            await AssertFavouriteAuthorizationProblemAsync(
                client, method, path, HttpStatusCode.Unauthorized,
                "customer_authentication_required", businessId, user);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateJwt(user.Id, role: "Admin"));
            await AssertFavouriteAuthorizationProblemAsync(
                client, method, path, HttpStatusCode.Forbidden,
                "customer_authorization_forbidden", businessId, user);
        }

        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .BusinessFavourites.CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-BUSINESS-PROJECTION-039")]
    public async Task CategoriesAndOfferingDetail_ProjectRatingsAndFavouriteForAnonymousAndCustomer()
    {
        var token = CatalogTestSupport.CreateToken(50);
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "projection-favourite@example.test",
            Email = "projection-favourite@example.test",
            FullName = "Projection Favourite User"
        };
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)],
            users: [user]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId));
        using var client = factory.CreateApiClient();
        using var browse = await client.SendAsync(CreateRequest(
            HttpMethod.Get, "/api/v1/catalog/businesses", token));
        using var browseJson = await ReadJsonAsync(browse);
        var business = browseJson.RootElement.GetProperty("businesses")[0];
        var businessId = business.GetProperty("id").GetGuid();
        var sourceId = business.GetProperty("sourceId").GetGuid();
        using var offerings = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{businessId:D}/offerings",
            token));
        using var offeringsJson = await ReadJsonAsync(offerings);
        var offeringId = offeringsJson.RootElement.GetProperty("offerings")[0]
            .GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.BusinessFavourites.Add(new BusinessFavourite
            {
                UserId = user.Id,
                BusinessSourceId = sourceId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            context.BusinessReviews.AddRange(
                CreateProjectionReview(user.Id, sourceId, 5),
                CreateProjectionReview(user.Id, sourceId, 4));
            await context.SaveChangesAsync();
        }

        using (var anonymousCategories = await client.SendAsync(CreateRequest(
                   HttpMethod.Get, "/api/v1/catalog/categories", token)))
        using (var document = await ReadJsonAsync(anonymousCategories))
        {
            AssertBusinessProjection(
                document.RootElement.GetProperty("categories")[0].GetProperty("business"),
                expectedFavourite: false);
        }
        using (var anonymousDetail = await client.SendAsync(CreateRequest(
                   HttpMethod.Get,
                   $"/api/v1/catalog/offerings/{offeringId:D}",
                   token)))
        using (var document = await ReadJsonAsync(anonymousDetail))
        {
            AssertBusinessProjection(
                document.RootElement.GetProperty("offering").GetProperty("business"),
                expectedFavourite: false);
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateJwt(user.Id));
        using (var customerCategories = await client.SendAsync(CreateRequest(
                   HttpMethod.Get, "/api/v1/catalog/categories", token)))
        using (var document = await ReadJsonAsync(customerCategories))
        {
            AssertBusinessProjection(
                document.RootElement.GetProperty("categories")[0].GetProperty("business"),
                expectedFavourite: true);
        }
        using (var customerDetail = await client.SendAsync(CreateRequest(
                   HttpMethod.Get,
                   $"/api/v1/catalog/offerings/{offeringId:D}",
                   token)))
        using (var document = await ReadJsonAsync(customerDetail))
        {
            AssertBusinessProjection(
                document.RootElement.GetProperty("offering").GetProperty("business"),
                expectedFavourite: true);
        }
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-BUSINESSES-061")]
    public async Task GetBusinesses_WithValidDeviceToken_ReturnsLocalizedBusinessesAndNoStore()
    {
        var token = CatalogTestSupport.CreateToken(9);
        await using var factory = new CatalogApiFactory(devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(
                companyId == factory.FirstSourceCompanyId
                    ? CatalogTestSupport.CreateSnapshot(
                        companyId,
                        version: 3,
                        companyNameAr: "الأول",
                        companyNameHe: "הראשון")
                    : CatalogTestSupport.CreateSnapshot(
                        companyId,
                        version: 4,
                        companyNameAr: "الثاني",
                        companyNameHe: "השני"));
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            deviceToken: token,
            acceptLanguage: "he");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        var businesses = document.RootElement.GetProperty("businesses").EnumerateArray().ToArray();
        businesses.Should().HaveCount(2);
        businesses[0].GetProperty("sourceId").GetGuid().Should().Be(factory.FirstSourceCompanyId);
        businesses[0].GetProperty("name").GetString().Should().Be("הראשון");
        businesses[0].GetProperty("catalog").GetProperty("version").GetInt64().Should().Be(3);
        businesses[1].GetProperty("catalog").GetProperty("version").GetInt64().Should().Be(4);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-CATEGORIES-060")]
    public async Task GetCategories_WithoutBusinessFilter_ReturnsOwningBusinessContext()
    {
        var token = CatalogTestSupport.CreateToken(10);
        await using var factory = new CatalogApiFactory(devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(
                companyId == factory.FirstSourceCompanyId
                    ? CatalogTestSupport.CreateSnapshot(
                        companyId,
                        version: 1,
                        companyNameAr: "شركة أ")
                    : CatalogTestSupport.CreateSnapshot(
                        companyId,
                        version: 1,
                        companyNameAr: "شركة ب"));
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/categories?language=ar",
            deviceToken: token);

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var categories = document.RootElement.GetProperty("categories").EnumerateArray().ToArray();
        categories.Should().HaveCount(2);
        foreach (var category in categories)
        {
            category.TryGetProperty("business", out var business).Should().BeTrue();
            business.TryGetProperty("id", out _).Should().BeTrue();
            business.TryGetProperty("sourceId", out _).Should().BeTrue();
            business.TryGetProperty("name", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetCategories_WithPresentationMetadata_ReturnsMetadataWithoutChangingLocalization()
    {
        var token = CatalogTestSupport.CreateToken(44);
        await using var factory = new CatalogApiFactory(devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(
                companyId,
                categoryNameAr: "غسيل خارجي",
                categoryNameHe: "שטיפה חיצונית",
                categoryImageUrl: "https://cdn.example.test/categories/exterior.png",
                categoryColorHex: "#1A73E8"));
        using var client = factory.CreateApiClient();

        using var response = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/categories?language=he",
            token));
        using var document = await ReadJsonAsync(response);
        var categories = document.RootElement.GetProperty("categories").EnumerateArray().ToArray();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        categories.Should().HaveCount(2);
        categories.Should().OnlyContain(category =>
            category.GetProperty("name").GetString() == "שטיפה חיצונית" &&
            category.GetProperty("imageUrl").GetString() ==
                "https://cdn.example.test/categories/exterior.png" &&
            category.GetProperty("colorHex").GetString() == "#1A73E8");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-CATEGORY-CUSTOMER-007")]
    [Trait("ScenarioId", "FAN-CATEGORY-LOCALIZATION-008")]
    public async Task GetCategories_AnonymousProductionDevice_ReturnsArabicAndExplicitNullMetadata()
    {
        var token = CatalogTestSupport.CreateToken(45);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(
                companyId,
                categoryNameAr: "غسيل عربي",
                categoryNameHe: "שטיפה",
                categoryImageUrl: null,
                categoryColorHex: null));
        using var client = factory.CreateApiClient();

        using var response = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/categories?language=ar",
            token));
        using var document = await ReadJsonAsync(response);
        var category = document.RootElement.GetProperty("categories")[0];

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
        category.GetProperty("name").GetString().Should().Be("غسيل عربي");
        category.GetProperty("imageUrl").ValueKind.Should().Be(JsonValueKind.Null);
        category.GetProperty("colorHex").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    [Trait("ScenarioId", "FAN-CATEGORY-LOCALIZATION-016")]
    public async Task GetCategories_HebrewMissing_FallsBackToArabicWithoutChangingMetadata()
    {
        var token = CatalogTestSupport.CreateToken(46);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(
                companyId,
                categoryNameAr: "الاسم العربي",
                categoryNameHe: null,
                categoryImageUrl: "https://cdn.example.test/categories/fallback.png",
                categoryColorHex: "#102030"));
        using var client = factory.CreateApiClient();

        using var response = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/categories?language=he",
            token));
        using var document = await ReadJsonAsync(response);
        var category = document.RootElement.GetProperty("categories")[0];

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        category.GetProperty("name").GetString().Should().Be("الاسم العربي");
        category.GetProperty("imageUrl").GetString().Should().EndWith("/fallback.png");
        category.GetProperty("colorHex").GetString().Should().Be("#102030");
    }

    [Fact]
    [Trait("ScenarioId", "FAN-CATEGORY-PARTITION-009")]
    [Trait("ScenarioId", "FAN-OPTIONAL-DEMO-019")]
    public async Task GetCategories_DemoDevice_ReturnsDemoCategoryMetadata()
    {
        var token = CatalogTestSupport.CreateToken(47);
        var device = CatalogTestSupport.CreateDevice(token);
        device.IsDemo = true;
        await using var factory = new CatalogApiFactory(
            devices: [device],
            useDemoProviders: true);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(
                companyId,
                categoryNameAr: "فئة ديمو",
                categoryNameHe: "קטגוריית דמו",
                categoryImageUrl: "https://cdn.example.test/categories/demo.png",
                categoryColorHex: "#D0E0F0"));
        using var client = factory.CreateApiClient();

        using var response = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/categories?language=ar",
            token));
        using var document = await ReadJsonAsync(response);
        var categories = document.RootElement.GetProperty("categories").EnumerateArray().ToArray();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        categories.Should().OnlyContain(category =>
            category.GetProperty("name").GetString() == "فئة ديمو" &&
            category.GetProperty("imageUrl").GetString()!.EndsWith("/demo.png") &&
            category.GetProperty("colorHex").GetString() == "#D0E0F0");
        document.RootElement.GetRawText().ToLowerInvariant().Should().NotContain("production");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-OFFERINGS-063")]
    public async Task GetBusinessOfferings_WithCrossProviderFilters_ReturnsStableMismatch()
    {
        var token = CatalogTestSupport.CreateToken(11);
        await using var factory = new CatalogApiFactory(devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(
                companyId == factory.FirstSourceCompanyId
                    ? CatalogTestSupport.CreateSnapshot(companyId, version: 1, companyNameAr: "الأول")
                    : CatalogTestSupport.CreateSnapshot(companyId, version: 1, companyNameAr: "الثاني"));
        using var client = factory.CreateApiClient();
        using var businessesRequest = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            deviceToken: token);

        using var businessesResponse = await client.SendAsync(businessesRequest);
        using var businessesDocument = await ReadJsonAsync(businessesResponse);
        var businesses = businessesDocument.RootElement.GetProperty("businesses").EnumerateArray().ToArray();
        var firstBusinessId = businesses[0].GetProperty("id").GetGuid();
        var foreignBranchId = businesses[1].GetProperty("branches").EnumerateArray().Single().GetProperty("id").GetGuid();

        using var categoriesRequest = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/categories",
            deviceToken: token);
        using var categoriesResponse = await client.SendAsync(categoriesRequest);
        using var categoriesDocument = await ReadJsonAsync(categoriesResponse);
        var categoryId = categoriesDocument.RootElement.GetProperty("categories")
            .EnumerateArray()
            .Single(category => category.GetProperty("business").GetProperty("id").GetGuid() == firstBusinessId)
            .GetProperty("id")
            .GetGuid();

        using var mismatchRequest = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{firstBusinessId:D}/offerings?categoryId={categoryId:D}&branchId={foreignBranchId:D}",
            deviceToken: token);
        using var response = await client.SendAsync(mismatchRequest);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString().Should().Be(CatalogProblemCodes.FilterMismatch);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-OFFERING-064")]
    public async Task GetOffering_WhenUnknown_ReturnsStableNotFound()
    {
        var token = CatalogTestSupport.CreateToken(12);
        await using var factory = new CatalogApiFactory(devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId, version: 1));
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/offerings/{Guid.NewGuid():D}",
            deviceToken: token,
            acceptLanguage: "he");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        document.RootElement.GetProperty("code").GetString().Should().Be(CatalogProblemCodes.OfferingNotFound);
        document.RootElement.GetProperty("language").GetString().Should().Be(ConfigurationLanguageResolver.Hebrew);
    }

    [Fact]
    public async Task OfferingMetadata_ListAndDetail_ReturnLocalizedQualifierAndStringBadge()
    {
        var token = CatalogTestSupport.CreateToken(41);
        var offeringSourceId = Guid.NewGuid();
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 2,
                offeringId: offeringSourceId,
                offeringQualifierAr: "بدون التعقيم",
                offeringQualifierHe: null,
                offeringBadgeCode: CatalogOfferingBadgeCode.MostRequested));
        using var client = factory.CreateApiClient();

        using var businessesRequest = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            token);
        using var businessesResponse = await client.SendAsync(businessesRequest);
        using var businessesDocument = await ReadJsonAsync(businessesResponse);
        var businessId = businessesDocument.RootElement.GetProperty("businesses")[0]
            .GetProperty("id").GetGuid();

        using var offeringsRequest = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{businessId:D}/offerings?language=he",
            token);
        using var offeringsResponse = await client.SendAsync(offeringsRequest);
        using var offeringsDocument = await ReadJsonAsync(offeringsResponse);
        var offering = offeringsDocument.RootElement.GetProperty("offerings")[0];
        var offeringId = offering.GetProperty("id").GetGuid();

        offeringsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        offeringsDocument.RootElement.GetProperty("language").GetString().Should().Be("he");
        offering.GetProperty("qualifier").GetString().Should().Be("بدون التعقيم");
        offering.GetProperty("badgeCode").GetString().Should().Be("MostRequested");

        using var detailRequest = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/offerings/{offeringId:D}?language=ar",
            token);
        using var detailResponse = await client.SendAsync(detailRequest);
        using var detailDocument = await ReadJsonAsync(detailResponse);
        var detailOffering = detailDocument.RootElement.GetProperty("offering");

        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        detailOffering.GetProperty("qualifier").GetString().Should().Be("بدون التعقيم");
        detailOffering.GetProperty("badgeCode").GetString().Should().Be("MostRequested");
    }

    [Fact]
    public async Task OfferingMetadata_WhenHebrewExists_ListAndDetailPreferHebrew()
    {
        var token = CatalogTestSupport.CreateToken(42);
        var offeringSourceId = Guid.NewGuid();
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 3,
                offeringId: offeringSourceId,
                offeringQualifierAr: "بدون التعقيم",
                offeringQualifierHe: "ללא חיטוי",
                offeringBadgeCode: CatalogOfferingBadgeCode.MostRequested));
        using var client = factory.CreateApiClient();

        using var businessesResponse = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            token));
        using var businessesDocument = await ReadJsonAsync(businessesResponse);
        var businessId = businessesDocument.RootElement.GetProperty("businesses")[0]
            .GetProperty("id").GetGuid();

        using var listResponse = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{businessId:D}/offerings?language=he",
            token));
        using var listDocument = await ReadJsonAsync(listResponse);
        var listOffering = listDocument.RootElement.GetProperty("offerings")[0];
        var offeringId = listOffering.GetProperty("id").GetGuid();

        using var detailResponse = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/offerings/{offeringId:D}?language=he",
            token));
        using var detailDocument = await ReadJsonAsync(detailResponse);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        listOffering.GetProperty("qualifier").GetString().Should().Be("ללא חיטוי");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        detailDocument.RootElement.GetProperty("offering").GetProperty("qualifier")
            .GetString().Should().Be("ללא חיטוי");
    }

    [Fact]
    public async Task OfferingMetadata_WhenAbsent_ListAndDetailReturnNullFields()
    {
        var token = CatalogTestSupport.CreateToken(43);
        var offeringSourceId = Guid.NewGuid();
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)]);
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(
                companyId,
                version: 4,
                offeringId: offeringSourceId));
        using var client = factory.CreateApiClient();

        using var businessesResponse = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            token));
        using var businessesDocument = await ReadJsonAsync(businessesResponse);
        var businessId = businessesDocument.RootElement.GetProperty("businesses")[0]
            .GetProperty("id").GetGuid();

        using var listResponse = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{businessId:D}/offerings?language=ar",
            token));
        using var listDocument = await ReadJsonAsync(listResponse);
        var listOffering = listDocument.RootElement.GetProperty("offerings")[0];
        var offeringId = listOffering.GetProperty("id").GetGuid();

        using var detailResponse = await client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/offerings/{offeringId:D}?language=he",
            token));
        using var detailDocument = await ReadJsonAsync(detailResponse);
        var detailOffering = detailDocument.RootElement.GetProperty("offering");

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        listOffering.GetProperty("qualifier").ValueKind.Should().Be(JsonValueKind.Null);
        listOffering.GetProperty("badgeCode").ValueKind.Should().Be(JsonValueKind.Null);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        detailOffering.GetProperty("qualifier").ValueKind.Should().Be(JsonValueKind.Null);
        detailOffering.GetProperty("badgeCode").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    [Trait("ScenarioId", "STEP20-SLOTS-CUSTOMER-001")]
    [Trait("ScenarioId", "FAN-OPTIONAL-CATALOG-SLOTS")]
    public async Task GetAvailableSlots_AnonymousProductionAndCatalogSelection_ReturnsNoStoreSlots()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId, version: 12));
        factory.BusinessApiClient.GetAvailableSlotsHandler = (request, _) =>
            Task.FromResult(new Ghseeli.IntegrationContracts.BusinessCatalog.AvailableSlotsResponse
            {
                Valid = true,
                CompanyId = request.CompanyId,
                BranchId = request.BranchId,
                Date = request.Date,
                TimeZoneId = "UTC",
                CatalogVersion = 12,
                Currency = "ILS",
                TotalDurationMinutes = 60,
                GeneratedAtUtc = DateTime.UtcNow,
                Slots =
                [
                    new Ghseeli.IntegrationContracts.BusinessCatalog.AvailableSlotResponse
                    {
                        StartUtc = request.Date.ToDateTime(new TimeOnly(9), DateTimeKind.Utc),
                        EndUtc = request.Date.ToDateTime(new TimeOnly(10), DateTimeKind.Utc),
                        StartLocal = request.Date.ToDateTime(new TimeOnly(9)),
                        EndLocal = request.Date.ToDateTime(new TimeOnly(10)),
                        ConfiguredCapacity = 3,
                        RemainingCapacity = 2,
                        IsAvailable = true
                    }
                ]
            });
        using var client = factory.CreateApiClient();
        var ids = await GetCatalogSelectionAsync(client, null);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/catalog/businesses/{ids.BusinessId:D}/branches/{ids.BranchId:D}/available-slots")
        {
            Content = JsonContent.Create(new
            {
                date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                items = new[] { new { offeringId = ids.OfferingId } }
            })
        };
        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        document.RootElement.GetProperty("businessId").GetGuid().Should().Be(ids.BusinessId);
        document.RootElement.GetProperty("branchId").GetGuid().Should().Be(ids.BranchId);
        document.RootElement.GetProperty("totalDurationMinutes").GetInt32().Should().Be(60);
        document.RootElement.GetProperty("slots")[0].GetProperty("remainingCapacity")
            .GetInt32().Should().Be(2);
    }

    [Fact]
    [Trait("ScenarioId", "STEP20-SLOTS-CUSTOMER-004")]
    public async Task GetAvailableSlots_WithoutDeviceToken_UsesAnonymousProduction()
    {
        await using var factory = new CatalogApiFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/catalog/businesses/{Guid.NewGuid():D}/branches/{Guid.NewGuid():D}/available-slots",
            new
            {
                date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                items = new[] { new { offeringId = Guid.NewGuid() } }
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.BusinessApiClient.AvailableSlotsRequests.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP20-SLOTS-CUSTOMER-010")]
    public async Task GetAvailableSlots_WithWrongContentType_ReturnsUnsupportedMediaType()
    {
        var token = CatalogTestSupport.CreateToken(21);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            $"/api/v1/catalog/businesses/{Guid.NewGuid():D}/branches/{Guid.NewGuid():D}/available-slots",
            token);
        request.Content = new StringContent("{}", Encoding.UTF8, "text/plain");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        factory.BusinessApiClient.AvailableSlotsRequests.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP20-SLOTS-CUSTOMER-011")]
    public async Task GetAvailableSlots_WithOversizedBody_ReturnsPayloadTooLarge()
    {
        var token = CatalogTestSupport.CreateToken(22);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)]);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Post,
            $"/api/v1/catalog/businesses/{Guid.NewGuid():D}/branches/{Guid.NewGuid():D}/available-slots",
            token);
        request.Content = new StringContent(
            $"{{\"padding\":\"{new string('x', 70_000)}\"}}",
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        factory.BusinessApiClient.AvailableSlotsRequests.Should().Be(0);
    }

    [Fact]
    public async Task Swagger_ContainsCatalogRoutes_AndCatalogServicesAreResolvable()
    {
        await using var factory = new CatalogApiFactory();
        factory.BusinessApiClient.GetCatalogSnapshotHandler = (companyId, _) =>
            Task.FromResult(CatalogTestSupport.CreateSnapshot(companyId, version: 1));
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = await ReadJsonAsync(response);
        using var scope = factory.Services.CreateScope();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paths = document.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/v1/catalog/categories", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/catalog/businesses", out _).Should().BeTrue();
        var businessBrowse = paths.GetProperty("/api/v1/catalog/businesses")
            .GetProperty("get");
        businessBrowse.GetProperty("parameters").EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .Should().Contain(["search", "top"]);
        paths.TryGetProperty("/api/v1/catalog/businesses/{id}", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/catalog/businesses/{id}/offerings", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/catalog/offerings/{id}", out _).Should().BeTrue();
        var favourite = paths.GetProperty(
            "/api/v1/catalog/businesses/{businessId}/favourite");
        foreach (var method in new[] { "put", "delete" })
        {
            var operation = favourite.GetProperty(method);
            operation.GetProperty("security").GetArrayLength().Should().Be(2);
            operation.GetProperty("responses").EnumerateObject()
                .Select(response => response.Name)
                .Should().Contain(["204", "401", "403", "404"]);
        }
        paths.TryGetProperty(
            "/api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots",
            out _).Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<ICatalogReadModelService>().Should().NotBeNull();
    }

    [Fact]
    public async Task Swagger_AndCatalogServicesResolve_WithSafeDefaultCatalogConfiguration()
    {
        await using var factory = new CatalogApiFactory(
            useActualBusinessApiClient: true,
            providers: Array.Empty<CatalogProviderRegistrationOptions>());
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = await ReadJsonAsync(response);
        using var scope = factory.Services.CreateScope();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("paths")
            .TryGetProperty("/api/v1/catalog/businesses", out _)
            .Should()
            .BeTrue();
        scope.ServiceProvider.GetRequiredService<ICatalogReadModelService>().Should().NotBeNull();
    }

    [Fact]
    public async Task GetBusinesses_WithNoConfiguredProviders_ReturnsIntentionalEmptyResponse()
    {
        var token = CatalogTestSupport.CreateToken(13);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)],
            providers: Array.Empty<CatalogProviderRegistrationOptions>(),
            useActualBusinessApiClient: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses?language=ar",
            deviceToken: token);

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        document.RootElement.GetProperty("language").GetString().Should().Be("ar");
        document.RootElement.GetProperty("businesses").EnumerateArray().ToArray().Should().BeEmpty();
    }

    [Fact]
    public async Task GetCategories_WithNoConfiguredProviders_ReturnsIntentionalEmptyResponse()
    {
        var token = CatalogTestSupport.CreateToken(14);
        await using var factory = new CatalogApiFactory(
            devices: [CatalogTestSupport.CreateDevice(token)],
            providers: Array.Empty<CatalogProviderRegistrationOptions>(),
            useActualBusinessApiClient: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/categories?language=he",
            deviceToken: token);

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
        document.RootElement.GetProperty("categories").EnumerateArray().ToArray().Should().BeEmpty();
    }

    [Fact]
    [Trait("ScenarioId", "FAN-OPTIONAL-UPSTREAM-012")]
    public async Task GetBusinesses_WhenBusinessApiClientConfigIsBlankAndRefreshIsRequired_ReturnsLocalizedUnavailableProblem()
    {
        await using var factory = new CatalogApiFactory(
            useActualBusinessApiClient: true);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses?language=he");

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        document.RootElement.GetProperty("code").GetString().Should().Be(CatalogProblemCodes.Unavailable);
        document.RootElement.GetProperty("language").GetString().Should().Be("he");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-CATALOG-DEMO-REFRESH-173")]
    public async Task GetBusinesses_WithDemoDevice_RefreshesFromDemoBusinessPartition()
    {
        var token = CatalogTestSupport.CreateToken(16);
        var device = CatalogTestSupport.CreateDevice(token);
        device.IsDemo = true;
        var sourceCompanyId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var staleRefreshAtUtc = DateTimeOffset.UtcNow.AddHours(-2);
        var staleProvider = new CatalogProviderReadModel
        {
            Id = Guid.NewGuid(),
            SourceCompanyId = sourceCompanyId,
            IsEnabled = true,
            IsDemo = true,
            DisplayOrder = 0,
            NameAr = "لقطة ديمو قديمة",
            CatalogVersion = 0,
            SnapshotHash = "stale-demo-snapshot",
            SnapshotGeneratedAtUtc = staleRefreshAtUtc,
            LastSuccessfulRefreshAtUtc = staleRefreshAtUtc
        };
        var businessApiHandler = new PartitionAwareCatalogHandler(sourceCompanyId);
        await using var factory = new CatalogApiFactory(
            devices: [device],
            catalogProviders: [staleProvider],
            providers:
            [
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = sourceCompanyId,
                    Enabled = true,
                    Order = 0
                }
            ],
            useActualBusinessApiClient: true,
            useDemoProviders: true,
            businessApiHandler: businessApiHandler);
        using var client = factory.CreateApiClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses?refresh=true",
            deviceToken: token);

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        businessApiHandler.RequestedPartitions.Should().ContainSingle(DataPartitionNames.Demo);
        var business = document.RootElement.GetProperty("businesses")
            .EnumerateArray()
            .Should()
            .ContainSingle()
            .Which;
        business.GetProperty("sourceId").GetGuid().Should().Be(sourceCompanyId);
        business.GetProperty("name").GetString().Should().NotBe("لقطة ديمو قديمة");
        var catalog = business.GetProperty("catalog");
        catalog.GetProperty("version").GetInt64().Should().Be(1);
        catalog.GetProperty("isStale").GetBoolean().Should().BeFalse();

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider
            .GetRequiredService<GhseeliApis.DataPartitioning.ICustomerDataPartitionContext>()
            .SetTrustedPartition(DataPartitionNames.Demo);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var refreshedProvider = await context.CatalogProviders.SingleAsync(
            provider => provider.SourceCompanyId == sourceCompanyId);
        refreshedProvider.NameAr.Should().NotBe("لقطة ديمو قديمة");
        refreshedProvider.CatalogVersion.Should().Be(1);
        refreshedProvider.LastSuccessfulRefreshAtUtc.Should().BeAfter(staleRefreshAtUtc);
    }

    [Fact]
    [Trait("ScenarioId", "STEP20-SLOTS-CUSTOMER-021")]
    public async Task GetAvailableSlots_WithDemoDevice_UsesDemoPartitionForRefreshAndSlots()
    {
        var token = CatalogTestSupport.CreateToken(17);
        var device = CatalogTestSupport.CreateDevice(token);
        device.IsDemo = true;
        var sourceCompanyId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var businessApiHandler = new PartitionAwareCatalogHandler(sourceCompanyId);
        await using var factory = new CatalogApiFactory(
            devices: [device],
            providers:
            [
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = sourceCompanyId,
                    Enabled = true,
                    Order = 0
                }
            ],
            useActualBusinessApiClient: true,
            useDemoProviders: true,
            businessApiHandler: businessApiHandler);
        using var client = factory.CreateApiClient();
        var ids = await GetCatalogSelectionAsync(client, token);
        using var request = CreateRequest(
            HttpMethod.Post,
            $"/api/v1/catalog/businesses/{ids.BusinessId:D}/branches/{ids.BranchId:D}/available-slots",
            deviceToken: token);
        request.Content = JsonContent.Create(new
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            items = new[] { new { offeringId = ids.OfferingId } }
        });

        using var response = await client.SendAsync(request);
        using var document = await ReadJsonAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        businessApiHandler.RequestedPartitions.Should()
            .Equal(DataPartitionNames.Demo, DataPartitionNames.Demo);
        businessApiHandler.RequestedPaths.Should().Equal(
            "/api/v1/internal/catalog/snapshot",
            "/api/v1/internal/appointments/available-slots");
        document.RootElement.GetProperty("totalDurationMinutes").GetInt32().Should().Be(60);
        document.RootElement.GetProperty("slots")[0].GetProperty("isAvailable")
            .GetBoolean()
            .Should()
            .BeTrue();
    }

    private static CatalogProviderReadModel CreateHiddenProvider(bool isEnabled) => new()
    {
        Id = Guid.NewGuid(),
        SourceCompanyId = Guid.NewGuid(),
        IsEnabled = isEnabled,
        NameAr = "مزود مخفي",
        CatalogVersion = 1,
        SnapshotHash = Guid.NewGuid().ToString("N"),
        LastSuccessfulRefreshAtUtc = DateTimeOffset.UtcNow
    };

    private static async Task AssertAnonymousProductionCatalogRouteAsync(
        AnonymousCatalogRoute route)
    {
        var sourceCompanyId = Guid.Parse("71717171-7171-7171-7171-717171717171");
        await using var factory = new CatalogApiFactory(
            providers:
            [
                new CatalogProviderRegistrationOptions
                {
                    SourceCompanyId = sourceCompanyId,
                    Enabled = true,
                    Order = 0
                }
            ]);
        using (var productionScope = factory.Services.CreateScope())
        {
            await CheckoutDraftTestSupport.SeedSnapshotAsync(
                productionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                CatalogTestSupport.CreateSnapshot(
                    sourceCompanyId,
                    companyNameAr: "PRODUCTION-ONLY-BUSINESS",
                    categoryNameAr: "PRODUCTION-ONLY-CATEGORY",
                    offeringNameAr: "PRODUCTION-ONLY-OFFERING"));
        }
        using (var demoScope = factory.Services.CreateScope())
        {
            demoScope.ServiceProvider
                .GetRequiredService<GhseeliApis.DataPartitioning.ICustomerDataPartitionContext>()
                .SetTrustedPartition(DataPartitionNames.Demo);
            await CheckoutDraftTestSupport.SeedSnapshotAsync(
                demoScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                CatalogTestSupport.CreateSnapshot(
                    sourceCompanyId,
                    companyNameAr: "DEMO-LEAK-SENTINEL-BUSINESS",
                    categoryNameAr: "DEMO-LEAK-SENTINEL-CATEGORY",
                    offeringNameAr: "DEMO-LEAK-SENTINEL-OFFERING"));
        }
        Guid businessId;
        Guid offeringId;
        using (var productionScope = factory.Services.CreateScope())
        {
            var provider = await productionScope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>()
                .CatalogProviders
                .Include(item => item.Categories)
                .ThenInclude(item => item.Offerings)
                .SingleAsync(item => item.SourceCompanyId == sourceCompanyId);
            businessId = provider.Id;
            offeringId = provider.Categories.Single().Offerings.Single().Id;
        }
        using var client = factory.CreateApiClient();
        var path = route switch
        {
            AnonymousCatalogRoute.Categories => "/api/v1/catalog/categories",
            AnonymousCatalogRoute.BusinessDetail =>
                $"/api/v1/catalog/businesses/{businessId:D}",
            AnonymousCatalogRoute.BusinessOfferings =>
                $"/api/v1/catalog/businesses/{businessId:D}/offerings",
            AnonymousCatalogRoute.OfferingDetail =>
                $"/api/v1/catalog/offerings/{offeringId:D}",
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

        using var response = await client.GetAsync(path);
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.Should().Contain("PRODUCTION-ONLY");
        payload.Should().NotContain("DEMO-LEAK-SENTINEL");
    }

    private enum AnonymousCatalogRoute
    {
        Categories,
        BusinessDetail,
        BusinessOfferings,
        OfferingDetail
    }

    private static BusinessReview CreateProjectionReview(
        Guid userId,
        Guid businessSourceId,
        int rating) => new()
    {
        CustomerBookingId = Guid.NewGuid(),
        UserId = userId,
        BusinessSourceId = businessSourceId,
        Rating = rating,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    private static void AssertBusinessProjection(
        JsonElement business,
        bool expectedFavourite)
    {
        business.GetProperty("isFavourite").GetBoolean().Should().Be(expectedFavourite);
        business.GetProperty("averageRating").GetDecimal().Should().Be(4.5m);
        business.GetProperty("ratingCount").GetInt32().Should().Be(2);
    }

    private static async Task AssertFavouriteAuthorizationProblemAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpStatusCode expectedStatus,
        string expectedCode,
        Guid businessId,
        User user)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(
            "X-Correlation-Id",
            $"favourite-auth-{method.Method.ToLowerInvariant()}-{(int)expectedStatus}");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType!.MediaType.Should()
            .Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Correlation-Id").Should()
            .Equal($"favourite-auth-{method.Method.ToLowerInvariant()}-{(int)expectedStatus}");
        response.Content.Headers.ContentLanguage.Should().Equal("ar");
        document.RootElement.GetProperty("code").GetString().Should().Be(expectedCode);
        document.RootElement.EnumerateObject().Select(value => value.Name)
            .Should().BeEquivalentTo(
                "type", "title", "status", "detail", "code", "correlationId",
                "language");
        body.ToLowerInvariant().Should().NotContain(businessId.ToString().ToLowerInvariant());
        body.ToLowerInvariant().Should().NotContain(user.Id.ToString().ToLowerInvariant());
        body.ToLowerInvariant().Should().NotContain(user.Email!.ToLowerInvariant());
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

        return request;
    }

    private static async Task<(Guid BusinessId, Guid BranchId, Guid OfferingId)>
        GetCatalogSelectionAsync(HttpClient client, string? token)
    {
        using var businessesRequest = CreateRequest(
            HttpMethod.Get,
            "/api/v1/catalog/businesses",
            token);
        using var businessesResponse = await client.SendAsync(businessesRequest);
        using var businessesDocument = await ReadJsonAsync(businessesResponse);
        var business = businessesDocument.RootElement.GetProperty("businesses")[0];
        var businessId = business.GetProperty("id").GetGuid();
        var branchId = business.GetProperty("branches")[0].GetProperty("id").GetGuid();

        using var offeringsRequest = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/catalog/businesses/{businessId:D}/offerings?branchId={branchId:D}",
            token);
        using var offeringsResponse = await client.SendAsync(offeringsRequest);
        using var offeringsDocument = await ReadJsonAsync(offeringsResponse);
        offeringsResponse.EnsureSuccessStatusCode();
        return (
            businessId,
            branchId,
            offeringsDocument.RootElement.GetProperty("offerings")[0]
                .GetProperty("id").GetGuid());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload);
    }

    private static DateOnly NextDay(DayOfWeek dayOfWeek)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        while (date.DayOfWeek != dayOfWeek)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private static string CreateJwt(
        Guid userId,
        string role = "User",
        string partition = DataPartitionNames.Production)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
            SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            JwtIssuer,
            JwtAudience,
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim(DataPartitionNames.ClaimType, partition)
            ],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials));
    }
}

public sealed class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly string _databaseName = $"CatalogApiTests-{Guid.NewGuid():N}";
    private readonly IEnumerable<CustomerDevice> _devices;
    private readonly IReadOnlyList<CatalogProviderRegistrationOptions> _providers;
    private readonly bool _useActualBusinessApiClient;
    private readonly bool _useDemoProviders;
    private readonly HttpMessageHandler? _businessApiHandler;
    private readonly IReadOnlyList<CatalogProviderReadModel> _catalogProviders;
    private readonly IReadOnlyList<User> _users;
    private readonly bool _demoPublicApisOnly;

    public CatalogApiFactory(
        IEnumerable<CustomerDevice>? devices = null,
        IEnumerable<CatalogProviderRegistrationOptions>? providers = null,
        IEnumerable<CatalogProviderReadModel>? catalogProviders = null,
        IEnumerable<User>? users = null,
        bool useActualBusinessApiClient = false,
        bool useDemoProviders = false,
        bool demoPublicApisOnly = false,
        HttpMessageHandler? businessApiHandler = null)
    {
        _devices = devices ?? Array.Empty<CustomerDevice>();
        _providers = providers?.ToArray() ?? CreateDefaultProviders();
        _catalogProviders = catalogProviders?.ToArray() ?? [];
        _users = users?.ToArray() ?? [];
        _useActualBusinessApiClient = useActualBusinessApiClient;
        _useDemoProviders = useDemoProviders;
        _demoPublicApisOnly = demoPublicApisOnly;
        _businessApiHandler = businessApiHandler;
    }

    public Guid FirstSourceCompanyId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public Guid SecondSourceCompanyId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal ScriptedBusinessApiClient BusinessApiClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:CustomerConnection", "Server=(localdb)\\MSSQLLocalDB;Database=CatalogApiTests;Trusted_Connection=True;TrustServerCertificate=True;");
        builder.UseSetting("JwtSettings:SecretKey", CatalogApiIntegrationTests.JwtSecret);
        builder.UseSetting("JwtSettings:Issuer", CatalogApiIntegrationTests.JwtIssuer);
        builder.UseSetting("JwtSettings:Audience", CatalogApiIntegrationTests.JwtAudience);
        builder.UseSetting("Swagger:Enabled", "true");
        builder.UseSetting(
            "DemoData:PublicApisOnly",
            _demoPublicApisOnly.ToString());
        builder.UseSetting("CatalogReadModel:FreshWindowSeconds", "300");
        builder.UseSetting("CatalogReadModel:MaxStaleWindowSeconds", "3600");
        builder.UseSetting("CatalogReadModel:LeaseDurationSeconds", "30");
        builder.UseSetting(
            "BusinessApiClient:BaseUrl",
            _businessApiHandler is null ? string.Empty : "https://business.example.test");
        builder.UseSetting("BusinessApiClient:ServiceId", "customer-api-tests");
        builder.UseSetting(
            "BusinessApiClient:ActiveSecret",
            "CustomerCatalogIntegrationSecret_Minimum32Chars");

        var providerSection = _useDemoProviders ? "DemoProviders" : "Providers";
        for (var index = 0; index < _providers.Count; index++)
        {
            builder.UseSetting(
                $"CatalogReadModel:{providerSection}:{index}:SourceCompanyId",
                _providers[index].SourceCompanyId.ToString());
            builder.UseSetting(
                $"CatalogReadModel:{providerSection}:{index}:Enabled",
                _providers[index].Enabled.ToString());
            builder.UseSetting(
                $"CatalogReadModel:{providerSection}:{index}:Order",
                _providers[index].Order.ToString());
        }

        if (_useDemoProviders)
        {
            for (var index = _providers.Count; index < 5; index++)
            {
                builder.UseSetting(
                    $"CatalogReadModel:DemoProviders:{index}:Enabled",
                    bool.FalseString);
            }
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            if (!_useActualBusinessApiClient)
            {
                services.RemoveAll<IBusinessApiClient>();
                services.AddSingleton<IBusinessApiClient>(_ => BusinessApiClient);
            }
            else if (_businessApiHandler is not null)
            {
                services.AddHttpClient<IBusinessApiClient, BusinessApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _businessApiHandler);
            }

            using var scope = services.BuildServiceProvider().CreateScope();
            if (_useDemoProviders)
            {
                scope.ServiceProvider
                    .GetRequiredService<GhseeliApis.DataPartitioning.ICustomerDataPartitionContext>()
                    .SetTrustedPartition(DataPartitionNames.Demo);
            }

            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
            if (_devices.Any())
            {
                context.CustomerDevices.AddRange(_devices);
            }
            if (_users.Count > 0)
            {
                context.Users.AddRange(_users);
            }
            if (_catalogProviders.Count > 0)
            {
                context.CatalogProviders.AddRange(_catalogProviders);
            }
            context.SaveChanges();
        });
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    public new async ValueTask DisposeAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.EnsureDeletedAsync();
        }
        catch
        {
        }

        await base.DisposeAsync();
    }

    private CatalogProviderRegistrationOptions[] CreateDefaultProviders() =>
    [
        new CatalogProviderRegistrationOptions
        {
            SourceCompanyId = FirstSourceCompanyId,
            Enabled = true,
            Order = 0
        },
        new CatalogProviderRegistrationOptions
        {
            SourceCompanyId = SecondSourceCompanyId,
            Enabled = true,
            Order = 1
        }
    ];
}

internal sealed class PartitionAwareCatalogHandler : HttpMessageHandler
{
    private readonly Guid _sourceCompanyId;
    private readonly Guid? _sourceBranchId;

    public PartitionAwareCatalogHandler(Guid sourceCompanyId, Guid? sourceBranchId = null)
    {
        _sourceCompanyId = sourceCompanyId;
        _sourceBranchId = sourceBranchId;
    }

    internal sealed class OversizedAvailabilityHandler : HttpMessageHandler
    {
        public Guid SourceCompanyId { get; } =
            Guid.Parse("27272727-2727-2727-2727-272727272727");
        public int AvailabilityRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(
                    "/appointments/availability-discovery",
                    StringComparison.Ordinal))
            {
                AvailabilityRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        new string('x', InternalServiceWireConstants.MaxStoredResponseBytes + 1),
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    CatalogTestSupport.CreateSnapshot(SourceCompanyId),
                    options: BusinessCatalogContract.CreateJsonSerializerOptions())
            });
        }
    }

    public List<string> RequestedPartitions { get; } = [];
    public List<string> RequestedPaths { get; } = [];
    public List<AvailabilityDiscoveryRequest> AvailabilityRequests { get; } = [];
    public List<string?> AvailabilitySignatureHeaders { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            request.RequestUri!.Query);
        var partition = query.TryGetValue(DataPartitionNames.QueryParameter, out var values)
            ? values.ToString()
            : string.Empty;
        RequestedPartitions.Add(partition);
        RequestedPaths.Add(request.RequestUri.AbsolutePath);

        if (!string.Equals(partition, DataPartitionNames.Demo, StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new { code = "company_not_found" })
            };
        }

        if (request.RequestUri.AbsolutePath.EndsWith(
                "/appointments/availability-discovery",
                StringComparison.Ordinal))
        {
            var availabilityRequest = await request.Content!.ReadFromJsonAsync<
                AvailabilityDiscoveryRequest>(
                BusinessCatalogContract.CreateJsonSerializerOptions(),
                cancellationToken);
            AvailabilityRequests.Add(availabilityRequest!);
            AvailabilitySignatureHeaders.Add(
                request.Headers.TryGetValues(
                    InternalServiceWireConstants.SignatureHeaderName,
                    out var signatureValues)
                    ? signatureValues.Single()
                    : null);
            var candidate = availabilityRequest!.Candidates.Single();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new AvailabilityDiscoveryResponse
                    {
                        ContractVersion = BusinessCatalogContract.Version,
                        Date = availabilityRequest.Date,
                        PreferredLocalTime = availabilityRequest.PreferredLocalTime,
                        GeneratedAtUtc = DateTime.UtcNow,
                        Results =
                        [
                            new AvailabilityDiscoveryCompanyResult
                            {
                                CompanyId = candidate.CompanyId,
                                BranchId = candidate.BranchIds.Single(),
                                TimeZoneId = "UTC",
                                SlotStartUtc = availabilityRequest.Date.ToDateTime(
                                    availabilityRequest.PreferredLocalTime,
                                    DateTimeKind.Utc),
                                SlotStartLocal = availabilityRequest.Date.ToDateTime(
                                    availabilityRequest.PreferredLocalTime),
                                ConfiguredCapacity = 2,
                                RemainingCapacity = 1
                            }
                        ]
                    },
                    options: BusinessCatalogContract.CreateJsonSerializerOptions())
            };
        }

        if (request.RequestUri.AbsolutePath.EndsWith(
                "/appointments/available-slots",
                StringComparison.Ordinal))
        {
            var slotsRequest = await request.Content!.ReadFromJsonAsync<
                Ghseeli.IntegrationContracts.BusinessCatalog.AvailableSlotsRequest>(
                Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract
                    .CreateJsonSerializerOptions(),
                cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new Ghseeli.IntegrationContracts.BusinessCatalog.AvailableSlotsResponse
                    {
                        Valid = true,
                        CompanyId = slotsRequest!.CompanyId,
                        BranchId = slotsRequest.BranchId,
                        Date = slotsRequest.Date,
                        TimeZoneId = "UTC",
                        CatalogVersion = slotsRequest.ExpectedCatalogVersion!.Value,
                        Currency = slotsRequest.Currency,
                        TotalDurationMinutes = 60,
                        GeneratedAtUtc = DateTime.UtcNow,
                        Slots =
                        [
                            new Ghseeli.IntegrationContracts.BusinessCatalog.AvailableSlotResponse
                            {
                                StartUtc = slotsRequest.Date.ToDateTime(
                                    new TimeOnly(9),
                                    DateTimeKind.Utc),
                                EndUtc = slotsRequest.Date.ToDateTime(
                                    new TimeOnly(10),
                                    DateTimeKind.Utc),
                                StartLocal = slotsRequest.Date.ToDateTime(new TimeOnly(9)),
                                EndLocal = slotsRequest.Date.ToDateTime(new TimeOnly(10)),
                                ConfiguredCapacity = 2,
                                RemainingCapacity = 1,
                                IsAvailable = true
                            }
                        ]
                    },
                    options: Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract
                        .CreateJsonSerializerOptions())
            };
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(
                CatalogTestSupport.CreateSnapshot(
                    _sourceCompanyId,
                    version: 1,
                    branchId: _sourceBranchId),
                options: Ghseeli.IntegrationContracts.InternalHttp.BusinessCatalogContract
                    .CreateJsonSerializerOptions())
        };
    }
}
