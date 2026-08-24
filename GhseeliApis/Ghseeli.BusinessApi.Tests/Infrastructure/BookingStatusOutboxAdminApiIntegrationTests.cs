using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Exercises global-admin authorization and idempotent dead-letter recovery over HTTP.
/// </summary>
public sealed class BookingStatusOutboxAdminApiIntegrationTests :
    IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public BookingStatusOutboxAdminApiIntegrationTests(CatalogApiFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Requeue_AnonymousOwnerAndEmployee_AreRejectedBeforeLookup()
    {
        var eventId = Guid.NewGuid();
        var anonymous = _factory.CreateSecureClient();
        var owner = _factory.CreateAuthenticatedClient(
            _factory.OwnerUserId, BusinessRoles.Owner);
        var employee = _factory.CreateAuthenticatedClient(
            _factory.EmployeeUserId, BusinessRoles.Employee);
        SetRequestId(anonymous, "anonymous-request");
        SetRequestId(owner, "owner-request");
        SetRequestId(employee, "employee-request");

        (await anonymous.PostAsync(Route(eventId), null)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await owner.PostAsync(Route(eventId), null)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await employee.PostAsync(Route(eventId), null)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-OUTBOX-REQUEUE-109")]
    public async Task Requeue_AdminDeadLetter_RequeuesAndRepeatIsIdempotent()
    {
        var eventId = await AddEventAsync(BookingStatusOutboxStates.DeadLetter);
        var admin = _factory.CreateAuthenticatedClient(
            _factory.AdminUserId, BusinessRoles.Admin);
        SetRequestId(admin, "admin-requeue-repeat");

        var first = await admin.PostAsync(Route(eventId), null);
        var second = await admin.PostAsync(Route(eventId), null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadFromJsonAsync<DeadLetterRequeueResponse>())!
            .Outcome.Should().Be(DeadLetterRequeueOutcomes.Requeued);
        (await second.Content.ReadFromJsonAsync<DeadLetterRequeueResponse>())!
            .Outcome.Should().Be(DeadLetterRequeueOutcomes.AlreadyRequeued);
        first.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-OUTBOX-REQUEUE-109")]
    public async Task Requeue_AdminUnknownId_ReturnsSafeNonEnumeratingNotFound()
    {
        var admin = _factory.CreateAuthenticatedClient(
            _factory.AdminUserId, BusinessRoles.Admin);
        SetRequestId(admin, "admin-requeue-unknown");

        var response = await admin.PostAsync(Route(Guid.NewGuid()), null);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        body.Should().Contain("booking_status_event_not_found");
        body.Should().NotContain("System.");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-BUSINESS-OUTBOX-REQUEUE-109")]
    public async Task Requeue_AdminLeasedEvent_ReturnsConflictWithoutMutation()
    {
        var eventId = await AddEventAsync(BookingStatusOutboxStates.Leased);
        var admin = _factory.CreateAuthenticatedClient(
            _factory.AdminUserId, BusinessRoles.Admin);
        SetRequestId(admin, "admin-requeue-leased");

        var response = await admin.PostAsync(Route(eventId), null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetStateAsync(eventId)).Should().Be(BookingStatusOutboxStates.Leased);
    }

    [Fact]
    public async Task Requeue_AdminNeverRequeuedDeliveredEvent_ReturnsConflict()
    {
        var eventId = await AddEventAsync(BookingStatusOutboxStates.Delivered);
        var admin = _factory.CreateAuthenticatedClient(
            _factory.AdminUserId, BusinessRoles.Admin);
        SetRequestId(admin, "admin-requeue-delivered");

        var response = await admin.PostAsync(Route(eventId), null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetStateAsync(eventId)).Should().Be(BookingStatusOutboxStates.Delivered);
    }

    [Fact]
    public async Task Swagger_DescribesAdminDeadLetterRequeueRoute()
    {
        var client = _factory.CreateSecureClient();

        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        swagger.GetProperty("paths")
            .TryGetProperty(
                "/api/v1/business/admin/booking-status-outbox/{eventId}/requeue",
                out var route)
            .Should().BeTrue();
        route.TryGetProperty("post", out _).Should().BeTrue();
    }

    private async Task<Guid> AddEventAsync(string state)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        var eventId = Guid.NewGuid();
        context.BookingStatusOutboxMessages.Add(new BookingStatusOutboxMessage
        {
            Id = eventId,
            AppointmentReservationId = Guid.NewGuid(),
            WorkOrderPublicId = Guid.NewGuid(),
            Status = "Confirmed",
            Sequence = 1,
            RequestJson = "{}",
            RequestHash = new string('a', 64),
            CorrelationId = "integration",
            DeliveryState = state,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            NextAttemptAtUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        return eventId;
    }

    private async Task<string> GetStateAsync(Guid eventId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        return (await context.BookingStatusOutboxMessages.FindAsync(eventId))!.DeliveryState;
    }

    private static string Route(Guid eventId) =>
        $"/api/v1/business/admin/booking-status-outbox/{eventId:D}/requeue";

    private static void SetRequestId(HttpClient client, string requestId) =>
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Idempotency-Key",
            requestId);
}
