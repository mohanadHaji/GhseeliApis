using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Defines the step-5 HTTP behavior for internal validation and business availability management.
/// </summary>
public class InternalCatalogAvailabilityIntegrationTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public InternalCatalogAvailabilityIntegrationTests(CatalogApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ValidateAppointment_WhenAnonymous_ReturnsUnauthorized()
    {
        _factory.ResetState();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/internal/appointments/validate", new
        {
            branchId = _factory.BranchId,
            offeringId = Guid.NewGuid(),
            requestedSlotStartUtc = DateTime.UtcNow.AddDays(1),
            currency = "ILS"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CatalogSnapshot_WhenAnonymous_ReturnsUnauthorized()
    {
        _factory.ResetState();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/internal/catalog/snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CompanyProfile_WhenAdminHasNoAssignment_ReturnsForbidden()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.AdminUserId, BusinessRoles.Admin);

        var response = await client.GetAsync("/api/v1/business/company");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateBranch_WhenAdminHasNoAssignment_ReturnsForbidden()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.AdminUserId, BusinessRoles.Admin);

        var response = await client.PostAsJsonAsync("/api/v1/business/company/branches", new
        {
            nameAr = "فرع إداري",
            addressAr = "الرياض"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AvailabilitySettings_UpsertThenGet_RoundTripsForOwnedBranch()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/settings",
            new
            {
                timeZoneId = "UTC",
                minimumLeadMinutes = 60,
                bookingHorizonDays = 30,
                isActive = true
            });
        var updateContent = await updateResponse.Content.ReadAsStringAsync();

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK, updateContent);

        var getResponse = await client.GetAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/settings");
        var getContent = await getResponse.Content.ReadAsStringAsync();

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, getContent);

        using var document = JsonDocument.Parse(getContent);
        document.RootElement.GetProperty("branchId").GetGuid().Should().Be(_factory.BranchId);
        document.RootElement.GetProperty("timeZoneId").GetString().Should().Be("UTC");
        document.RootElement.GetProperty("minimumLeadMinutes").GetInt32().Should().Be(60);
        document.RootElement.GetProperty("bookingHorizonDays").GetInt32().Should().Be(30);
        document.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAppointment_WhenCatalogAndAvailabilityAreConfigured_ReturnsAuthoritativeFacts()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var configured = await ConfigureCatalogAndAvailabilityAsync(client);

        var nextMondayAtNineUtc = GetNextUtcDay(DayOfWeek.Monday).AddHours(9);

        var validateResponse = await client.PostAsJsonAsync("/api/v1/internal/appointments/validate", new
        {
            branchId = configured.BranchId,
            offeringId = configured.Offering.Id,
            selectedAddons = new[]
            {
                new
                {
                    addonChoiceId = configured.AddonChoiceId,
                    quantity = 2
                }
            },
            requestedSlotStartUtc = nextMondayAtNineUtc,
            customerLocation = new
            {
                latitude = 24.7136,
                longitude = 46.6753
            },
            expectedCatalogVersion = configured.CatalogVersion,
            currency = "ILS"
        });
        var validateContent = await validateResponse.Content.ReadAsStringAsync();

        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK, validateContent);

        using var validateDocument = JsonDocument.Parse(validateContent);
        var root = validateDocument.RootElement;
        root.GetProperty("valid").GetBoolean().Should().BeTrue(validateContent);
        root.GetProperty("catalogVersion").GetInt64().Should().Be(configured.CatalogVersion);
        root.GetProperty("currency").GetString().Should().Be("ILS");
        root.GetProperty("baseSubtotal").GetDecimal().Should().Be(50.00m);
        root.GetProperty("addonSubtotal").GetDecimal().Should().Be(20.02m);
        root.GetProperty("totalPrice").GetDecimal().Should().Be(70.02m);
        root.GetProperty("totalDurationMinutes").GetInt32().Should().Be(75);
        root.GetProperty("availability").GetProperty("configuredCapacity").GetInt32().Should().Be(4);
        root.GetProperty("availability").GetProperty("capacityReservationChecked").GetBoolean().Should().BeFalse();
        root.GetProperty("serviceArea").GetProperty("isWithinServiceArea").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAppointment_WhenTimestampHasPositiveOffset_NormalizesToUtc()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var configured = await ConfigureCatalogAndAvailabilityAsync(client);

        var validateResponse = await client.PostAsJsonAsync("/api/v1/internal/appointments/validate", new
        {
            branchId = configured.BranchId,
            offeringId = configured.Offering.Id,
            selectedAddons = new[]
            {
                new
                {
                    addonChoiceId = configured.AddonChoiceId,
                    quantity = 1
                }
            },
            requestedSlotStartUtc = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.FromHours(3)),
            customerLocation = new
            {
                latitude = 24.7136,
                longitude = 46.6753
            },
            expectedCatalogVersion = configured.CatalogVersion,
            currency = "ILS"
        });
        var validateContent = await validateResponse.Content.ReadAsStringAsync();

        validateResponse.StatusCode.Should().Be(HttpStatusCode.OK, validateContent);

        using var validateDocument = JsonDocument.Parse(validateContent);
        var availability = validateDocument.RootElement.GetProperty("availability");
        validateDocument.RootElement.GetProperty("valid").GetBoolean().Should().BeTrue(validateContent);
        availability.GetProperty("requestedSlotStartUtc").GetDateTime()
            .Should()
            .Be(new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc));
        availability.GetProperty("requestedSlotEndUtc").GetDateTime()
            .Should()
            .Be(new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task CatalogVersion_ReadsDoNotMutateVersion_ButAvailabilityChangesDo()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        var initialVersion = await GetSnapshotVersionAsync(client);
        var repeatedVersion = await GetSnapshotVersionAsync(client);

        initialVersion.Should().Be(repeatedVersion);

        (await client.PutAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/settings",
            new
            {
                timeZoneId = "UTC",
                minimumLeadMinutes = 30,
                bookingHorizonDays = 20,
                isActive = true
            })).EnsureSuccessStatusCode();

        var mutatedVersion = await GetSnapshotVersionAsync(client);

        mutatedVersion.Should().BeGreaterThan(initialVersion);
    }

    [Fact]
    public async Task CatalogVersion_WhenCatalogMutates_Increments()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        var initialVersion = await GetSnapshotVersionAsync(client);
        _ = await CreateCategoryAsync(client, "جديد", true);
        var mutatedVersion = await GetSnapshotVersionAsync(client);

        mutatedVersion.Should().BeGreaterThan(initialVersion);
    }

    [Fact]
    public async Task CatalogSnapshot_ReturnsOnlyActivePublishedEntities()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        var activeCategory = await CreateCategoryAsync(client, "نشط", true);
        var inactiveCategory = await CreateCategoryAsync(client, "مخفي", false);

        var activeOffering = await CreateOfferingAsync(client, activeCategory.Id, _factory.BranchId, "معلن", true);
        _ = await CreateOfferingAsync(client, activeCategory.Id, _factory.BranchId, "غير منشور", false);
        _ = await CreateOfferingAsync(client, inactiveCategory.Id, _factory.BranchId, "تابع لمخفي", true);

        await CreateAddonGroupAsync(client, activeOffering.Id, "نشط", AddonSelectionType.QuantityCounter, true, choiceActive: true);
        await CreateAddonGroupAsync(client, activeOffering.Id, "مخفي", AddonSelectionType.QuantityCounter, false, choiceActive: true);

        var snapshotResponse = await client.GetAsync("/api/v1/internal/catalog/snapshot");
        var snapshotContent = await snapshotResponse.Content.ReadAsStringAsync();

        snapshotResponse.StatusCode.Should().Be(HttpStatusCode.OK, snapshotContent);

        using var document = JsonDocument.Parse(snapshotContent);
        var categories = document.RootElement.GetProperty("categories");
        categories.GetArrayLength().Should().Be(1);
        categories[0].GetProperty("nameAr").GetString().Should().Be("نشط");

        var offerings = categories[0].GetProperty("offerings");
        offerings.GetArrayLength().Should().Be(1);
        offerings[0].GetProperty("nameAr").GetString().Should().Be("معلن");

        var groups = offerings[0].GetProperty("addonGroups");
        groups.GetArrayLength().Should().Be(1);
        groups[0].GetProperty("nameAr").GetString().Should().Be("نشط");
        groups[0].GetProperty("choices").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ValidateAppointment_WhenCatalogVersionIsStale_ReturnsStableError()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var configured = await ConfigureCatalogAndAvailabilityAsync(client);

        var response = await ValidateAsync(
            client,
            configured.BranchId,
            configured.Offering.Id,
            configured.AddonChoiceId,
            configured.CatalogVersion - 1,
            24.7136,
            46.6753,
            GetNextUtcDay(DayOfWeek.Monday).AddHours(9));
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        using var document = JsonDocument.Parse(content);
        document.RootElement.GetProperty("valid").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("catalogVersion").GetInt64().Should().Be(configured.CatalogVersion);
        document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("STALE_CATALOG_VERSION");
    }

    [Fact]
    public async Task ValidateAppointment_WhenCustomerIsOutsideServiceArea_ReturnsStableError()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var configured = await ConfigureCatalogAndAvailabilityAsync(client);

        var response = await ValidateAsync(
            client,
            configured.BranchId,
            configured.Offering.Id,
            configured.AddonChoiceId,
            configured.CatalogVersion,
            0d,
            0d,
            GetNextUtcDay(DayOfWeek.Monday).AddHours(9));
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        using var document = JsonDocument.Parse(content);
        document.RootElement.GetProperty("valid").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("errors")
            .EnumerateArray()
            .Select(element => element.GetProperty("code").GetString())
            .Should()
            .Contain("OUT_OF_SERVICE_AREA");
    }

    [Fact]
    public async Task ValidateAppointment_WhenDateOverrideClosesRequestedDate_ReturnsUnavailable()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var configured = await ConfigureCatalogAndAvailabilityAsync(client);
        var nextMonday = DateOnly.FromDateTime(GetNextUtcDay(DayOfWeek.Monday));

        (await client.PostAsJsonAsync(
            $"/api/v1/business/availability/branches/{configured.BranchId}/date-overrides",
            new
            {
                overrideDate = nextMonday,
                isClosed = true,
                isActive = true
            })).EnsureSuccessStatusCode();

        var response = await ValidateAsync(
            client,
            configured.BranchId,
            configured.Offering.Id,
            configured.AddonChoiceId,
            configured.CatalogVersion + 1,
            24.7136,
            46.6753,
            GetNextUtcDay(DayOfWeek.Monday).AddHours(9));
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        using var document = JsonDocument.Parse(content);
        document.RootElement.GetProperty("valid").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("availability").GetProperty("usedDateOverride").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("errors")
            .EnumerateArray()
            .Select(element => element.GetProperty("code").GetString())
            .Should()
            .Contain("SLOT_UNAVAILABLE");
    }

    [Fact]
    public async Task UpdateBranch_WhenRemovingCoordinatesWouldInvalidateImplicitServiceArea_ReturnsBadRequest()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        (await client.PutAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/service-area",
            new
            {
                radiusKm = 15.5,
                isActive = true
            })).EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/business/company/branches/{_factory.BranchId}",
            new
            {
                nameAr = "الرئيسي",
                addressAr = "العنوان",
                latitude = (double?)null,
                longitude = (double?)null,
                isActive = true
            });
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, content);
        content.Should().ContainEquivalentOf("latitude");
        content.Should().ContainEquivalentOf("service area");
    }

    [Fact]
    public async Task CatalogSnapshot_WhenCompanyIsInactive_ReturnsNotFound()
    {
        _factory.ResetState();
        _factory.MutateState(context =>
        {
            context.Companies.Single(company => company.Id == _factory.CompanyId).IsActive = false;
        });

        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var response = await client.GetAsync("/api/v1/internal/catalog/snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CatalogSnapshot_WhenLegacyImplicitServiceAreaCannotResolve_SuppressesTheBrokenArea()
    {
        _factory.ResetState();
        _factory.MutateState(context =>
        {
            var branch = context.Branches.Single(item => item.Id == _factory.BranchId);
            branch.Latitude = null;
            branch.Longitude = null;
            context.BranchServiceAreas.Add(new BranchServiceArea
            {
                BranchId = branch.Id,
                RadiusKm = 10d,
                IsActive = true
            });
        });

        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var response = await client.GetAsync("/api/v1/internal/catalog/snapshot");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        using var document = JsonDocument.Parse(content);
        document.RootElement.GetProperty("serviceAreas").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task CreateOffering_WhenBasePriceExceedsOperationalLimit_ReturnsBadRequest()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var category = await CreateCategoryAsync(client, "تنظيف", true);

        var response = await client.PostAsJsonAsync("/api/v1/business/catalog/offerings",
            new CreateServiceOfferingRequest
            {
                CategoryId = category.Id,
                BranchId = _factory.BranchId,
                NameAr = "غسيل متكامل",
                BasePrice = 1_000_000.01m,
                DurationMinutes = 45,
                DisplayOrder = 0,
                IsActive = true
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOffering_WhenValuesMatchExactBoundaries_ReturnsCreated()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);
        var category = await CreateCategoryAsync(client, "تنظيف", true);

        var response = await client.PostAsJsonAsync("/api/v1/business/catalog/offerings",
            new CreateServiceOfferingRequest
            {
                CategoryId = category.Id,
                BranchId = _factory.BranchId,
                NameAr = "غسيل حدّي",
                BasePrice = 1_000_000m,
                DurationMinutes = 1_440,
                DisplayOrder = 0,
                IsActive = true
            });
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Created, content);
    }

    [Fact]
    public async Task ValidateAppointment_WhenTotalPriceExceedsSupportedRange_ReturnsStableSelectionViolation()
    {
        _factory.ResetState();
        var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, BusinessRoles.Owner);

        var category = await CreateCategoryAsync(client, "حدود السعر", true);
        var offeringResponse = await client.PostAsJsonAsync("/api/v1/business/catalog/offerings",
            new CreateServiceOfferingRequest
            {
                CategoryId = category.Id,
                BranchId = _factory.BranchId,
                NameAr = "خدمة الحد الأعلى",
                BasePrice = 1_000_000m,
                DurationMinutes = 30,
                DisplayOrder = 0,
                IsActive = true
            });
        var offeringContent = await offeringResponse.Content.ReadAsStringAsync();
        offeringResponse.StatusCode.Should().Be(HttpStatusCode.Created, offeringContent);
        var offering = await offeringResponse.Content.ReadFromJsonAsync<ServiceOfferingResponse>();

        var addonGroupResponse = await client.PostAsJsonAsync(
            $"/api/v1/business/catalog/offerings/{offering!.Id}/addon-groups",
            new CreateAddonGroupRequest
            {
                NameAr = "إضافة سعرية",
                SelectionType = AddonSelectionType.QuantityCounter,
                IsRequired = false,
                MinimumSelections = 0,
                MaximumSelections = 2,
                DisplayOrder = 0,
                IsActive = true,
                Choices =
                [
                    new CreateAddonChoiceRequest
                    {
                        NameAr = "زيادة طفيفة",
                        PriceAdjustment = 0.01m,
                        DurationAdjustmentMinutes = 0,
                        DefaultQuantity = 0,
                        DisplayOrder = 0,
                        IsActive = true
                    }
                ]
            });
        var addonGroupContent = await addonGroupResponse.Content.ReadAsStringAsync();
        addonGroupResponse.StatusCode.Should().Be(HttpStatusCode.Created, addonGroupContent);
        var addonGroup = await addonGroupResponse.Content.ReadFromJsonAsync<AddonGroupResponse>();
        var addonChoiceId = addonGroup!.Choices.Single().Id;

        (await client.PutAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/settings",
            new
            {
                timeZoneId = "UTC",
                minimumLeadMinutes = 0,
                bookingHorizonDays = 30,
                isActive = true
            })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/recurring-schedules",
            new
            {
                dayOfWeek = "Monday",
                startLocalTime = "09:00:00",
                endLocalTime = "18:00:00",
                slotDurationMinutes = 15,
                capacity = 4,
                isActive = true
            })).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/service-area",
            new
            {
                radiusKm = 15.5,
                centerLatitude = 24.7136,
                centerLongitude = 46.6753,
                isActive = true
            })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/v1/internal/appointments/validate", new
        {
            branchId = _factory.BranchId,
            offeringId = offering.Id,
            selectedAddons = new[]
            {
                new
                {
                    addonChoiceId,
                    quantity = 1
                }
            },
            requestedSlotStartUtc = GetNextUtcDay(DayOfWeek.Monday).AddHours(9),
            customerLocation = new
            {
                latitude = 24.7136,
                longitude = 46.6753
            },
            expectedCatalogVersion = await GetSnapshotVersionAsync(client),
            currency = "ILS"
        });
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, content);

        using var document = JsonDocument.Parse(content);
        document.RootElement.GetProperty("valid").GetBoolean().Should().BeFalse(content);
        document.RootElement.GetProperty("errors")
            .EnumerateArray()
            .Select(element => element.GetProperty("code").GetString())
            .Should()
            .Contain("ADDON_SELECTION_RULE_VIOLATION");
    }

    private static DateTime GetNextUtcDay(DayOfWeek dayOfWeek)
    {
        var currentDate = DateTime.UtcNow.Date;
        var offset = ((int)dayOfWeek - (int)currentDate.DayOfWeek + 7) % 7;
        offset = offset == 0 ? 7 : offset;
        return currentDate.AddDays(offset);
    }

    private async Task<(Guid BranchId, ServiceOfferingResponse Offering, Guid AddonChoiceId, long CatalogVersion)> ConfigureCatalogAndAvailabilityAsync(
        HttpClient client)
    {
        var category = await CreateCategoryAsync(client, "تنظيف", true);
        var offering = await CreateOfferingAsync(client, category.Id, _factory.BranchId, "غسيل متكامل", true);
        var addonChoiceId = await CreateAddonGroupAsync(
            client,
            offering.Id,
            "إضافات",
            AddonSelectionType.QuantityCounter,
            true,
            choiceActive: true);

        (await client.PutAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/settings",
            new
            {
                timeZoneId = "UTC",
                minimumLeadMinutes = 0,
                bookingHorizonDays = 30,
                isActive = true
            })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/recurring-schedules",
            new
            {
                dayOfWeek = "Monday",
                startLocalTime = "09:00:00",
                endLocalTime = "18:00:00",
                slotDurationMinutes = 15,
                capacity = 4,
                isActive = true
            })).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/service-area",
            new
            {
                radiusKm = 15.5,
                centerLatitude = 24.7136,
                centerLongitude = 46.6753,
                isActive = true
            })).EnsureSuccessStatusCode();

        return (_factory.BranchId, offering, addonChoiceId, await GetSnapshotVersionAsync(client));
    }

    private async Task<ServiceCategoryResponse> CreateCategoryAsync(
        HttpClient client,
        string nameAr,
        bool isActive)
    {
        var response = await client.PostAsJsonAsync("/api/v1/business/catalog/categories",
            new CreateServiceCategoryRequest
            {
                NameAr = nameAr,
                DisplayOrder = 0,
                IsActive = isActive
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServiceCategoryResponse>())!;
    }

    private async Task<ServiceOfferingResponse> CreateOfferingAsync(
        HttpClient client,
        Guid categoryId,
        Guid branchId,
        string nameAr,
        bool isActive)
    {
        var response = await client.PostAsJsonAsync("/api/v1/business/catalog/offerings",
            new CreateServiceOfferingRequest
            {
                CategoryId = categoryId,
                BranchId = branchId,
                NameAr = nameAr,
                BasePrice = 49.995m,
                DurationMinutes = 45,
                DisplayOrder = 0,
                IsActive = isActive
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServiceOfferingResponse>())!;
    }

    private async Task<Guid> CreateAddonGroupAsync(
        HttpClient client,
        Guid offeringId,
        string nameAr,
        AddonSelectionType selectionType,
        bool groupActive,
        bool choiceActive)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/business/catalog/offerings/{offeringId}/addon-groups",
            new CreateAddonGroupRequest
            {
                NameAr = nameAr,
                SelectionType = selectionType,
                IsRequired = false,
                MinimumSelections = 0,
                MaximumSelections = 3,
                DisplayOrder = 0,
                IsActive = groupActive,
                Choices =
                [
                    new CreateAddonChoiceRequest
                    {
                        NameAr = "شمع",
                        PriceAdjustment = 10.005m,
                        DurationAdjustmentMinutes = 15,
                        DefaultQuantity = 0,
                        DisplayOrder = 0,
                        IsActive = choiceActive
                    }
                ]
            });
        response.EnsureSuccessStatusCode();
        var addonGroup = (await response.Content.ReadFromJsonAsync<AddonGroupResponse>())!;
        return addonGroup.Choices.Single().Id;
    }

    private static async Task<long> GetSnapshotVersionAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/internal/catalog/snapshot");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);
        using var document = JsonDocument.Parse(content);
        return document.RootElement.GetProperty("catalogVersion").GetInt64();
    }

    private async Task<HttpResponseMessage> ValidateAsync(
        HttpClient client,
        Guid branchId,
        Guid offeringId,
        Guid addonChoiceId,
        long expectedCatalogVersion,
        double latitude,
        double longitude,
        DateTime requestedSlotStartUtc)
    {
        return await client.PostAsJsonAsync("/api/v1/internal/appointments/validate", new
        {
            branchId,
            offeringId,
            selectedAddons = new[]
            {
                new
                {
                    addonChoiceId,
                    quantity = 1
                }
            },
            requestedSlotStartUtc,
            customerLocation = new
            {
                latitude,
                longitude
            },
            expectedCatalogVersion,
            currency = "ILS"
        });
    }
}
