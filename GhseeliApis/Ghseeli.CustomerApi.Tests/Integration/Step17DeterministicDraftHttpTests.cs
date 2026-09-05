using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GhseeliApis.Persistence;
using GhseeliApis.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Proves draft expiry and optimistic-concurrency behavior through Customer HTTP.
/// </summary>
public sealed class Step17DeterministicDraftHttpTests
{
    [Fact]
    [Trait("ScenarioId", "STEP17-DET-DRAFT-009")]
    public async Task STEP17_DET_DRAFT_009_GetAtExpiryReturnsGone()
    {
        var token = CatalogTestSupport.CreateToken(177);
        await using var factory = CreateFactory(token);
        using var client = factory.CreateApiClient();
        var orderGuid = await Step17CustomerHttpTestSupport.CreateDraftAsync(factory, client, token);
        factory.TimeProvider.Advance(TimeSpan.FromMinutes(30));
        var before = await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid);
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Get,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=he",
            token,
            correlationId: "corr-step17-det-draft-009");

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Gone,
            "checkout_draft_expired",
            "he",
            "תוקף הטיוטה פג.",
            "תוקף טיוטת התשלום פג ולא ניתן להשתמש בה.",
            "corr-step17-det-draft-009");

        (await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid))
            .Should().Be(before);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-DRAFT-010")]
    public async Task STEP17_DET_DRAFT_010_UpdateExpiredDraftReturnsGone()
    {
        var token = CatalogTestSupport.CreateToken(178);
        await using var factory = CreateFactory(token);
        using var client = factory.CreateApiClient();
        var orderGuid = await Step17CustomerHttpTestSupport.CreateDraftAsync(factory, client, token);
        factory.TimeProvider.Advance(TimeSpan.FromMinutes(30));
        var before = await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid);
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Put,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=ar",
            token,
            JsonContent.Create(Step17CustomerHttpTestSupport.UpdateRequest(
                factory.Snapshot,
                expectedVersion: 1)),
            correlationId: "corr-step17-det-draft-010");

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Gone,
            "checkout_draft_expired",
            "ar",
            "انتهت صلاحية المسودة.",
            "انتهت صلاحية مسودة الدفع ولا يمكن استخدامها.",
            "corr-step17-det-draft-010");

        (await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid))
            .Should().Be(before);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-DRAFT-011")]
    public async Task STEP17_DET_DRAFT_011_RepriceAlreadyExpiredDraftSkipsBusiness()
    {
        var token = CatalogTestSupport.CreateToken(179);
        await using var factory = CreateFactory(token);
        using var client = factory.CreateApiClient();
        var orderGuid = await Step17CustomerHttpTestSupport.CreateDraftAsync(factory, client, token);
        (await Step17CustomerHttpTestSupport.RepriceDraftAsync(
            client,
            token,
            orderGuid,
            expectedVersion: 1)).Should().Be(2);
        factory.TimeProvider.Advance(TimeSpan.FromMinutes(30));
        var before = await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid);
        var validationCalls = factory.BusinessApiClient.ValidateAppointmentRequests;
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/checkout/reprice?language=ar",
            token,
            JsonContent.Create(new { expectedVersion = 2 }),
            correlationId: "corr-step17-det-draft-011");
        request.Headers.TryAddWithoutValidation("X-Order-Guid", orderGuid.ToString("D"));

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Gone,
            "checkout_draft_expired",
            "ar",
            "انتهت صلاحية المسودة.",
            "انتهت صلاحية مسودة الدفع ولا يمكن إعادة تسعيرها.",
            "corr-step17-det-draft-011");

        factory.BusinessApiClient.ValidateAppointmentRequests.Should().Be(validationCalls);
        (await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid))
            .Should().Be(before);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-DRAFT-012")]
    public async Task STEP17_DET_DRAFT_012_ConfirmExpiredDraftReturnsGoneBeforeClaim()
    {
        var token = CatalogTestSupport.CreateToken(180);
        await using var factory = CreateFactory(token);
        using var client = factory.CreateApiClient();
        var orderGuid = await Step17CustomerHttpTestSupport.CreateDraftAsync(factory, client, token);
        (await Step17CustomerHttpTestSupport.RepriceDraftAsync(
            client,
            token,
            orderGuid,
            expectedVersion: 1)).Should().Be(2);
        var userId = Guid.NewGuid();
        await Step17CustomerHttpTestSupport.SeedUserAsync(factory, userId, "expired-step17");
        factory.TimeProvider.Advance(TimeSpan.FromMinutes(30));
        var before = await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid);
        var businessCalls = factory.BusinessApiClient.CreateReservationRequests;
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/bookings/from-draft?language=he",
            token,
            JsonContent.Create(new
            {
                expectedVersion = 2,
                cancellationPolicyAcknowledged = true
            }),
            Step17CustomerHttpTestSupport.CustomerJwt(userId),
            "corr-step17-det-draft-012");
        request.Headers.TryAddWithoutValidation("X-Order-Guid", orderGuid.ToString("D"));

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Gone,
            "checkout_draft_expired",
            "he",
            "לא ניתן לאשר את ההזמנה.",
            "תוקף טיוטת התשלום פג.",
            "corr-step17-det-draft-012");

        factory.BusinessApiClient.CreateReservationRequests.Should().Be(businessCalls);
        (await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid))
            .Should().Be(before);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.BookingConfirmationAttempts.CountAsync()).Should().Be(0);
        (await context.CustomerBookings.CountAsync()).Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-DRAFT-013")]
    public async Task STEP17_DET_DRAFT_013_StalePutLeavesDraftUnchanged()
    {
        var token = CatalogTestSupport.CreateToken(181);
        await using var factory = CreateFactory(token);
        using var client = factory.CreateApiClient();
        var orderGuid = await Step17CustomerHttpTestSupport.CreateDraftAsync(factory, client, token);
        await PutAsync(factory, client, token, orderGuid, expectedVersion: 1, "Green", 2);
        await PutAsync(factory, client, token, orderGuid, expectedVersion: 2, "Black", 3);
        var before = await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid);
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Put,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=ar",
            token,
            JsonContent.Create(Step17CustomerHttpTestSupport.UpdateRequest(
                factory.Snapshot,
                expectedVersion: 2,
                color: "White")),
            correlationId: "corr-step17-det-draft-013");

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "checkout_draft_version_conflict",
            "ar",
            "حدث تعارض في مسودة الدفع.",
            "تم تعديل مسودة الدفع. حدّث البيانات وأعد المحاولة.",
            "corr-step17-det-draft-013");

        (await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid))
            .Should().Be(before);
    }

    [Fact]
    [Trait("ScenarioId", "STEP17-DET-DRAFT-014")]
    public async Task STEP17_DET_DRAFT_014_StaleRepriceSkipsBusiness()
    {
        var token = CatalogTestSupport.CreateToken(182);
        await using var factory = CreateFactory(token);
        using var client = factory.CreateApiClient();
        var orderGuid = await Step17CustomerHttpTestSupport.CreateDraftAsync(factory, client, token);
        (await Step17CustomerHttpTestSupport.RepriceDraftAsync(
            client,
            token,
            orderGuid,
            expectedVersion: 1)).Should().Be(2);
        await PutAsync(factory, client, token, orderGuid, expectedVersion: 2, "Black", 3);
        var before = await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid);
        var validationCalls = factory.BusinessApiClient.ValidateAppointmentRequests;
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Post,
            "/api/v1/checkout/reprice?language=he",
            token,
            JsonContent.Create(new { expectedVersion = 2 }),
            correlationId: "corr-step17-det-draft-014");
        request.Headers.TryAddWithoutValidation("X-Order-Guid", orderGuid.ToString("D"));

        using var response = await client.SendAsync(request);
        using var problem = await Step17CustomerHttpTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "checkout_draft_version_conflict",
            "he",
            "בקשת התמחור התנגשה.",
            "טיוטת התשלום השתנתה. רענן ונסה שוב.",
            "corr-step17-det-draft-014");

        factory.BusinessApiClient.ValidateAppointmentRequests.Should().Be(validationCalls);
        (await Step17CustomerHttpTestSupport.ReadDraftStateAsync(factory, orderGuid))
            .Should().Be(before);
    }

    private static CheckoutDraftApiFactory CreateFactory(string token) =>
        Step17CustomerHttpTestSupport.CreateFactory(
        [
            CatalogTestSupport.CreateDevice(
                token,
                Step17CustomerHttpTestSupport.FixedNow.AddDays(1))
        ]);

    private static async Task PutAsync(
        CheckoutDraftApiFactory factory,
        HttpClient client,
        string token,
        Guid orderGuid,
        int expectedVersion,
        string color,
        int resultingVersion)
    {
        using var request = Step17CustomerHttpTestSupport.Request(
            HttpMethod.Put,
            $"/api/v1/checkout/drafts/{orderGuid:D}?language=ar",
            token,
            JsonContent.Create(Step17CustomerHttpTestSupport.UpdateRequest(
                factory.Snapshot,
                expectedVersion,
                color)));
        using var response = await client.SendAsync(request);
        using var document = await Step17CustomerHttpTestSupport.JsonAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, document.RootElement.GetRawText());
        document.RootElement.GetProperty("version").GetInt32().Should().Be(resultingVersion);
    }
}
