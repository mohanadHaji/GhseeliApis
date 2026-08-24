using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.IntegrationContracts.Bookings;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Infrastructure;

/// <summary>
/// Verifies typed Business JWT failures and non-enumerating work-order ownership.
/// </summary>
public sealed class WorkOrderTransitionAuthorizationIntegrationTests :
    IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;

    public WorkOrderTransitionAuthorizationIntegrationTests(CatalogApiFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Transition_WhenAnonymous_ReturnsTypedUnauthorizedWithoutMutation()
    {
        var workOrderId = SeedPendingWorkOrder();
        using var client = _factory.CreateSecureClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "step13-anonymous");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "step13-anonymous-transition");

        var response = await client.PostAsJsonAsync(
            Route(workOrderId),
            new TransitionWorkOrderRequest { Status = BookingStatuses.Confirmed });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Correlation-Id").Should()
            .ContainSingle("step13-anonymous-transition");
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(BusinessAuthenticationProblemCodes.AuthenticationRequired);
        document.RootElement.GetProperty("correlationId").GetString()
            .Should().Be("step13-anonymous-transition");
        body.Should().NotContain("System.");
        AssertUnchanged(workOrderId);
    }

    [Fact]
    public async Task Transition_WhenJwtIsExpired_ReturnsAuthenticationRequiredWithoutMutation()
    {
        var workOrderId = SeedPendingWorkOrder();
        using var client = _factory.CreateExpiredAuthenticatedClient(
            _factory.OwnerUserId,
            BusinessRoles.Owner);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "step13-expired");

        var response = await client.PostAsJsonAsync(
            Route(workOrderId),
            new TransitionWorkOrderRequest { Status = BookingStatuses.Confirmed });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(BusinessAuthenticationProblemCodes.AuthenticationRequired);
        AssertUnchanged(workOrderId);
    }

    [Fact]
    public async Task Transition_WhenRoleIsNotBusinessMember_ReturnsTypedForbiddenWithoutMutation()
    {
        var workOrderId = SeedPendingWorkOrder();
        using var client = _factory.CreateAuthenticatedClient(_factory.OwnerUserId, "User");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "step13-wrong-role-key");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "step13-wrong-role");

        var response = await client.PostAsJsonAsync(
            Route(workOrderId),
            new TransitionWorkOrderRequest { Status = BookingStatuses.Confirmed });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be("business_authorization_forbidden");
        document.RootElement.GetProperty("correlationId").GetString()
            .Should().Be("step13-wrong-role");
        AssertUnchanged(workOrderId);
    }

    [Fact]
    public async Task Transition_WhenOwnerBelongsToAnotherCompany_ReturnsNotFoundWithoutMutation()
    {
        var workOrderId = SeedPendingWorkOrder();
        using var client = _factory.CreateAuthenticatedClient(
            _factory.OtherOwnerUserId,
            BusinessRoles.Owner);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "step13-foreign");

        var response = await client.PostAsJsonAsync(
            Route(workOrderId),
            new TransitionWorkOrderRequest { Status = BookingStatuses.Confirmed });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        AssertUnchanged(workOrderId);
    }

    private Guid SeedPendingWorkOrder()
    {
        _factory.ResetState();
        var workOrderId = Guid.NewGuid();
        _factory.MutateState(context =>
        {
            context.AppointmentReservations.Add(new AppointmentReservation
            {
                PublicId = Guid.NewGuid(),
                CustomerBookingReference = Guid.NewGuid(),
                OrderGuid = Guid.NewGuid(),
                RequestHash = new string('a', 64),
                BranchId = _factory.BranchId,
                CatalogVersion = 1,
                Currency = "ILS",
                RequestedSlotStartUtc = DateTime.UtcNow.AddDays(1),
                RequestedSlotEndUtc = DateTime.UtcNow.AddDays(1).AddHours(1),
                Status = BookingStatuses.Pending,
                StatusSequence = 0,
                StatusChangedAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                WorkOrder = new WorkOrder
                {
                    PublicId = workOrderId,
                    Status = BookingStatuses.Pending,
                    CustomerName = "Test customer",
                    VehicleType = "Sedan",
                    AddressLine = "Test address",
                    CreatedAtUtc = DateTime.UtcNow
                }
            });
        });
        return workOrderId;
    }

    private void AssertUnchanged(Guid workOrderId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        context.WorkOrders.Single(value => value.PublicId == workOrderId).Status
            .Should().Be(BookingStatuses.Pending);
        context.AppointmentReservations.Single(value => value.WorkOrder.PublicId == workOrderId)
            .StatusSequence.Should().Be(0);
        context.BookingStatusOutboxMessages.Should().BeEmpty();
    }

    private static string Route(Guid workOrderId) =>
        $"/api/v1/business/work-orders/{workOrderId:D}/transitions";
}
