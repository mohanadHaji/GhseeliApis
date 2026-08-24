using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Integration;

/// <summary>
/// Keeps every Step 15 Business operation behind the normalized Business HTTP boundary.
/// </summary>
public class Step15BusinessDomainHttpTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public Step15BusinessDomainHttpTests(CatalogApiFactory factory) => _factory = factory;

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-COMPANY-READ-080")]
    [Trait("ScenarioId", "STEP15-LANG-INVARIANCE-016")]
    public async Task CompanyRead_UsesSelectedLanguageWithoutLeakingUnselectedOrEnglishValues()
    {
        _factory.ResetState();
        using var client = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);

        var arabicResponse = await client.GetAsync("/api/v1/business/company?language=ar");
        var hebrewResponse = await client.GetAsync("/api/v1/business/company?language=he");
        var arabic = await arabicResponse.Content.ReadAsStringAsync();
        var hebrew = await hebrewResponse.Content.ReadAsStringAsync();

        arabicResponse.StatusCode.Should().Be(HttpStatusCode.OK, arabic);
        hebrewResponse.StatusCode.Should().Be(HttpStatusCode.OK, hebrew);
        arabicResponse.Content.Headers.ContentLanguage.Should().ContainSingle("ar");
        hebrewResponse.Content.Headers.ContentLanguage.Should().ContainSingle("he");
        arabic.Should().Contain("شركة الاختبار").And.NotContain("חברת בדיקה");
        hebrew.Should().Contain("חברת בדיקה").And.NotContain("شركة الاختبار");
        arabic.Should().NotContain("Catalog Test User");
        hebrew.Should().NotContain("Catalog Test User");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-COMPANY-UPDATE-081")]
    [Trait("ScenarioId", "STEP15-BUSINESS-BRANCH-CREATE-082")]
    [Trait("ScenarioId", "STEP15-BUSINESS-BRANCH-UPDATE-083")]
    public async Task CompanyAndBranchMutations_NormalizeBlankOptionalHebrewToNull()
    {
        _factory.ResetState();
        using var client = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);

        var companyResponse = await client.PutAsJsonAsync(
            "/api/v1/business/company?language=ar",
            new { nameAr = "شركة عربية", nameHe = "   ", descriptionAr = "وصف عربي", descriptionHe = "\t" });
        var branchResponse = await client.PostAsJsonAsync(
            "/api/v1/business/company/branches?language=ar",
            new
            {
                nameAr = "فرع عربي",
                nameHe = " ",
                addressAr = "عنوان عربي",
                addressHe = "\t",
                latitude = 24.7,
                longitude = 46.6
            });

        companyResponse.StatusCode.Should().Be(HttpStatusCode.OK, await companyResponse.Content.ReadAsStringAsync());
        branchResponse.StatusCode.Should().Be(HttpStatusCode.Created, await branchResponse.Content.ReadAsStringAsync());
        using var company = JsonDocument.Parse(await companyResponse.Content.ReadAsStringAsync());
        using var branch = JsonDocument.Parse(await branchResponse.Content.ReadAsStringAsync());
        company.RootElement.GetProperty("nameHe").ValueKind.Should().Be(JsonValueKind.Null);
        company.RootElement.GetProperty("descriptionHe").ValueKind.Should().Be(JsonValueKind.Null);
        branch.RootElement.GetProperty("nameHe").ValueKind.Should().Be(JsonValueKind.Null);
        branch.RootElement.GetProperty("addressHe").ValueKind.Should().Be(JsonValueKind.Null);

        var branchId = branch.RootElement.GetProperty("id").GetGuid();
        var updateBranchResponse = await client.PutAsJsonAsync(
            $"/api/v1/business/company/branches/{branchId:D}?language=he",
            new
            {
                nameAr = "فرع محدث",
                nameHe = "סניף מעודכן",
                addressAr = "عنوان محدث",
                addressHe = "כתובת מעודכנת",
                latitude = 24.8,
                longitude = 46.7,
                isActive = false
            });
        var updateBody = await updateBranchResponse.Content.ReadAsStringAsync();
        updateBranchResponse.StatusCode.Should().Be(HttpStatusCode.OK, updateBody);
        using var updated = JsonDocument.Parse(updateBody);
        updated.RootElement.GetProperty("nameHe").GetString().Should().Be("סניף מעודכן");
        updated.RootElement.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-CATEGORY-CREATE-086")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-CATEGORY-READ-085")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-CATEGORY-LIST-084")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-CATEGORY-UPDATE-087")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-CATEGORY-DELETE-088")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-OFFERING-LIST-089")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-OFFERING-READ-090")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-OFFERING-CREATE-091")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-OFFERING-UPDATE-092")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-OFFERING-DELETE-093")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-GROUP-LIST-094")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-GROUP-READ-095")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-GROUP-CREATE-096")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-GROUP-UPDATE-097")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-GROUP-DELETE-098")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-CHOICE-LIST-099")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-CHOICE-READ-100")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-CHOICE-CREATE-101")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-CHOICE-UPDATE-102")]
    [Trait("ScenarioId", "STEP15-BUSINESS-CATALOG-CHOICE-DELETE-103")]
    public async Task CatalogMutations_NormalizeOptionalHebrewAndHebrewReadFallsBackPerField()
    {
        _factory.ResetState();
        using var client = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);
        var categoryResponse = await client.PostAsJsonAsync(
            "/api/v1/business/catalog/categories?language=ar",
            new { nameAr = "تنظيف", nameHe = " ", descriptionAr = "وصف", descriptionHe = "\t", displayOrder = 0, isActive = true });
        var categoryBody = await categoryResponse.Content.ReadAsStringAsync();
        categoryResponse.StatusCode.Should().Be(HttpStatusCode.Created, categoryBody);
        using var category = JsonDocument.Parse(categoryBody);
        var categoryId = category.RootElement.GetProperty("id").GetGuid();
        category.RootElement.GetProperty("nameHe").ValueKind.Should().Be(JsonValueKind.Null);

        var offeringResponse = await client.PostAsJsonAsync(
            "/api/v1/business/catalog/offerings?language=ar",
            new
            {
                categoryId,
                nameAr = "غسيل",
                nameHe = " ",
                basePrice = 45.25m,
                durationMinutes = 30,
                displayOrder = 0,
                isActive = true
            });
        var offeringBody = await offeringResponse.Content.ReadAsStringAsync();
        offeringResponse.StatusCode.Should().Be(HttpStatusCode.Created, offeringBody);
        using var offering = JsonDocument.Parse(offeringBody);
        var offeringId = offering.RootElement.GetProperty("id").GetGuid();
        offering.RootElement.GetProperty("nameHe").ValueKind.Should().Be(JsonValueKind.Null);

        var groupResponse = await client.PostAsJsonAsync(
            $"/api/v1/business/catalog/offerings/{offeringId}/addon-groups?language=ar",
            new
            {
                nameAr = "إضافات",
                nameHe = " ",
                selectionType = "MultipleChoice",
                isRequired = false,
                minimumSelections = 0,
                maximumSelections = 2,
                displayOrder = 0,
                isActive = true,
                choices = new[]
                {
                    new
                    {
                        nameAr = "أساسي",
                        nameHe = (string?)null,
                        priceAdjustment = 0m,
                        durationAdjustmentMinutes = 0,
                        defaultQuantity = 0,
                        displayOrder = 0,
                        isActive = true
                    }
                }
            });
        var groupBody = await groupResponse.Content.ReadAsStringAsync();
        groupResponse.StatusCode.Should().Be(HttpStatusCode.Created, groupBody);
        using var group = JsonDocument.Parse(groupBody);
        var groupId = group.RootElement.GetProperty("id").GetGuid();
        group.RootElement.GetProperty("nameHe").ValueKind.Should().Be(JsonValueKind.Null);

        var choiceResponse = await client.PostAsJsonAsync(
            $"/api/v1/business/catalog/addon-groups/{groupId}/choices?language=ar",
            new
            {
                nameAr = "شمع",
                nameHe = " ",
                priceAdjustment = 5m,
                durationAdjustmentMinutes = 5,
                defaultQuantity = 0,
                displayOrder = 0,
                isActive = true
            });
        var choiceBody = await choiceResponse.Content.ReadAsStringAsync();
        choiceResponse.StatusCode.Should().Be(HttpStatusCode.Created, choiceBody);
        using var choice = JsonDocument.Parse(choiceBody);
        choice.RootElement.GetProperty("nameHe").ValueKind.Should().Be(JsonValueKind.Null);

        var fallbackResponse = await client.GetAsync(
            $"/api/v1/business/catalog/categories/{categoryId}?language=he");
        var fallbackBody = await fallbackResponse.Content.ReadAsStringAsync();
        fallbackResponse.StatusCode.Should().Be(HttpStatusCode.OK, fallbackBody);
        fallbackResponse.Content.Headers.ContentLanguage.Should().ContainSingle("he");
        fallbackBody.Should().Contain("تنظيف");
        fallbackBody.Should().NotContain("Catalog");

        var categoryUpdate = await client.PutAsJsonAsync(
            $"/api/v1/business/catalog/categories/{categoryId:D}?language=ar",
            new { nameAr = "تنظيف محدث", nameHe = "ניקוי", displayOrder = 2, isActive = true });
        categoryUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        var offeringUpdate = await client.PutAsJsonAsync(
            $"/api/v1/business/catalog/offerings/{offeringId:D}?language=ar",
            new
            {
                nameAr = "غسيل محدث", nameHe = "שטיפה", basePrice = 50m,
                durationMinutes = 35, displayOrder = 1, isActive = true
            });
        offeringUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        var groupUpdate = await client.PutAsJsonAsync(
            $"/api/v1/business/catalog/addon-groups/{groupId:D}?language=ar",
            new
            {
                nameAr = "إضافات محدثة", nameHe = "תוספות", selectionType = "MultipleChoice",
                isRequired = false, minimumSelections = 0, maximumSelections = 2,
                displayOrder = 1, isActive = true
            });
        groupUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        var choiceId = choice.RootElement.GetProperty("id").GetGuid();
        var choiceUpdate = await client.PutAsJsonAsync(
            $"/api/v1/business/catalog/addon-choices/{choiceId:D}?language=ar",
            new
            {
                nameAr = "شمع محدث", nameHe = "שעווה", priceAdjustment = 6m,
                durationAdjustmentMinutes = 6, defaultQuantity = 0,
                displayOrder = 1, isActive = true
            });
        choiceUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondCategory = await client.PostAsJsonAsync(
            "/api/v1/business/catalog/categories?language=ar",
            new { nameAr = "أولاً", displayOrder = 0, isActive = true });
        secondCategory.StatusCode.Should().Be(HttpStatusCode.Created);
        var categories = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/business/catalog/categories?language=ar");
        categories.EnumerateArray().Select(item => item.GetProperty("displayOrder").GetInt32())
            .Should().BeInAscendingOrder();

        var filteredOfferings = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/business/catalog/offerings?categoryId={categoryId:D}&language=ar");
        filteredOfferings.EnumerateArray().Should().ContainSingle();
        (await client.GetAsync($"/api/v1/business/catalog/offerings/{offeringId:D}?language=ar"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/business/catalog/offerings/{offeringId:D}/addon-groups?language=ar"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/business/catalog/addon-groups/{groupId:D}?language=ar"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/business/catalog/addon-groups/{groupId:D}/choices?language=ar"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/business/catalog/addon-choices/{choiceId:D}?language=ar"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.DeleteAsync($"/api/v1/business/catalog/addon-choices/{choiceId:D}?language=ar"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/business/catalog/addon-choices/{choiceId:D}?language=ar"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/v1/business/catalog/addon-groups/{groupId:D}?language=ar"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/v1/business/catalog/offerings/{offeringId:D}?language=ar"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/v1/business/catalog/categories/{categoryId:D}?language=ar"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/business/catalog/categories/{categoryId:D}?language=ar"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-AVAILABILITY-SETTINGS-104")]
    [Trait("ScenarioId", "STEP15-BUSINESS-AVAILABILITY-SCHEDULES-105")]
    [Trait("ScenarioId", "STEP15-BUSINESS-AVAILABILITY-OVERRIDES-106")]
    [Trait("ScenarioId", "STEP15-BUSINESS-AVAILABILITY-SERVICE-AREA-107")]
    public async Task AvailabilityOperations_EnforceRulesAndCompleteCrudLifecycle()
    {
        _factory.ResetState();
        using var client = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);

        var settings = await client.PutAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/settings?language=he",
            new { timeZoneId = "Asia/Jerusalem", minimumLeadMinutes = 30, bookingHorizonDays = 14, isActive = true });
        var schedule = await client.PostAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/recurring-schedules?language=he",
            new
            {
                dayOfWeek = "Monday", startLocalTime = "09:00:00", endLocalTime = "12:00:00",
                slotDurationMinutes = 30, capacity = 2, isActive = true
            });
        var scheduleBody = await schedule.Content.ReadAsStringAsync();
        schedule.StatusCode.Should().Be(HttpStatusCode.OK, scheduleBody);
        using var scheduleJson = JsonDocument.Parse(scheduleBody);
        var scheduleId = scheduleJson.RootElement.GetProperty("id").GetGuid();
        var overlap = await client.PostAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/recurring-schedules?language=he",
            new
            {
                dayOfWeek = "Monday", startLocalTime = "11:30:00", endLocalTime = "13:00:00",
                slotDurationMinutes = 30, capacity = 1, isActive = true
            });
        overlap.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await overlap.Content.ReadAsStringAsync()).Should().NotContain("overlap");

        var overrideDate = new DateOnly(2026, 9, 14);
        var availabilityOverride = await client.PostAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/date-overrides?language=he",
            new
            {
                overrideDate, isClosed = false, startLocalTime = "10:00:00",
                endLocalTime = "14:00:00", slotDurationMinutes = 30, capacity = 3, isActive = true
            });
        var overrideBody = await availabilityOverride.Content.ReadAsStringAsync();
        availabilityOverride.StatusCode.Should().Be(HttpStatusCode.OK, overrideBody);
        using var overrideJson = JsonDocument.Parse(overrideBody);
        var overrideId = overrideJson.RootElement.GetProperty("id").GetGuid();
        var serviceArea = await client.PutAsJsonAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/service-area?language=he",
            new { centerLatitude = (double?)null, centerLongitude = (double?)null, radiusKm = 12.5, isActive = true });

        foreach (var response in new[] { settings, schedule, availabilityOverride, serviceArea })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
            response.Content.Headers.ContentLanguage.Should().ContainSingle("he");
            Step15BusinessHttpTestSupport.AssertSecurityHeaders(response);
            Step15BusinessHttpTestSupport.AssertRedacted(await response.Content.ReadAsStringAsync());
        }

        var schedules = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/recurring-schedules?language=he");
        schedules.EnumerateArray().Should().ContainSingle();
        var rangedOverrides = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/date-overrides?fromDate=2026-09-01&toDate=2026-09-30&language=he");
        rangedOverrides.EnumerateArray().Should().ContainSingle();

        var scheduleUpdate = await client.PutAsJsonAsync(
            $"/api/v1/business/availability/recurring-schedules/{scheduleId:D}?language=he",
            new
            {
                dayOfWeek = "Tuesday", startLocalTime = "09:00:00", endLocalTime = "11:00:00",
                slotDurationMinutes = 30, capacity = 4, isActive = true
            });
        scheduleUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        var overrideUpdate = await client.PutAsJsonAsync(
            $"/api/v1/business/availability/date-overrides/{overrideId:D}?language=he",
            new { overrideDate, isClosed = true, isActive = true });
        overrideUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.DeleteAsync(
            $"/api/v1/business/availability/recurring-schedules/{scheduleId:D}?language=he"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync(
            $"/api/v1/business/availability/recurring-schedules/{scheduleId:D}?language=he"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync(
            $"/api/v1/business/availability/date-overrides/{overrideId:D}?language=he"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/service-area?language=he"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync(
            $"/api/v1/business/availability/branches/{_factory.BranchId}/service-area?language=he"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-WORKORDER-TRANSITION-108")]
    [Trait("ScenarioId", "STEP15-BUSINESS-OUTBOX-REQUEUE-109")]
    public async Task BookingOperations_EnforceRoleBeforeDisclosingMissingIdentifiers()
    {
        using var employee = _factory.CreateAuthenticatedClient(_factory.EmployeeUserId, BusinessRoles.Employee);
        using var owner = Step15BusinessHttpTestSupport.CreateOwnerClient(_factory);
        var identifier = Guid.NewGuid();
        employee.DefaultRequestHeaders.TryAddWithoutValidation(
            "Idempotency-Key", "step15-work-order-missing");
        owner.DefaultRequestHeaders.TryAddWithoutValidation(
            "Idempotency-Key", "step15-outbox-owner-forbidden");

        var workOrder = await employee.PostAsJsonAsync(
            $"/api/v1/business/work-orders/{identifier}/transitions?language=ar",
            new { status = "Confirmed" });
        var outbox = await owner.PostAsync(
            $"/api/v1/business/admin/booking-status-outbox/{identifier}/requeue?language=ar",
            content: null);

        workOrder.StatusCode.Should().Be(HttpStatusCode.NotFound);
        outbox.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await workOrder.Content.ReadAsStringAsync()).Should().NotContain(identifier.ToString());
        (await outbox.Content.ReadAsStringAsync()).Should().NotContain(identifier.ToString());
    }
}
