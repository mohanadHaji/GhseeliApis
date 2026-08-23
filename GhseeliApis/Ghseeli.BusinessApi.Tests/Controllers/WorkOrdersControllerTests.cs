using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Controllers;
using Ghseeli.BusinessApi.Services;
using Ghseeli.IntegrationContracts.Bookings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Controllers;

/// <summary>
/// Verifies stable HTTP mapping for work-order transition persistence races.
/// </summary>
public sealed class WorkOrdersControllerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task Transition_WhenNameIdentifierIsMissingOrMalformed_ReturnsTypedUnauthorizedWithoutMutation(
        string? nameIdentifier)
    {
        var service = new Mock<IBookingStatusService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, nameIdentifier);

        var result = await controller.Transition(
            Guid.NewGuid(),
            new TransitionWorkOrderRequest { Status = BookingStatuses.Confirmed },
            CancellationToken.None);

        result.Should().BeOfType<EmptyResult>();
        controller.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        controller.Response.ContentType.Should().Be("application/problem+json");
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        controller.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(controller.Response.Body);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(BusinessAuthenticationProblemCodes.AuthenticationRequired);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Transition_WhenPersistenceDetectsConcurrency_ReturnsStableConflictProblem()
    {
        var service = new Mock<IBookingStatusService>();
        service.Setup(value => value.TransitionAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("database detail"));
        var controller = new WorkOrdersController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "transition-race"
                }
            }
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, BusinessRoles.Owner)
        ], "Test"));

        var result = await controller.Transition(
            Guid.NewGuid(),
            new TransitionWorkOrderRequest { Status = BookingStatuses.Confirmed },
            CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        content.ContentType.Should().Be("application/problem+json");
        using var document = JsonDocument.Parse(content.Content!);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(BookingStatusErrorCodes.TransitionConflict);
        content.Content.Should().NotContain("database detail");
    }

    private static WorkOrdersController CreateController(
        IBookingStatusService service,
        string? nameIdentifier)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, BusinessRoles.Owner)
        };
        if (nameIdentifier is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, nameIdentifier));
        }
        return new WorkOrdersController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "invalid-principal",
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
                    Response = { Body = new MemoryStream() }
                }
            }
        };
    }
}
