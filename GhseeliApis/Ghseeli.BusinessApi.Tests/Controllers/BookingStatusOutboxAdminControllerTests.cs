using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Controllers;
using Ghseeli.BusinessApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Tests.Controllers;

/// <summary>
/// Verifies the administrative requeue HTTP contract and safe problem responses.
/// </summary>
public sealed class BookingStatusOutboxAdminControllerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task Requeue_WhenNameIdentifierIsMissingOrMalformed_ReturnsTypedUnauthorizedWithoutMutation(
        string? nameIdentifier)
    {
        var service = new Mock<IBookingStatusDeadLetterService>(MockBehavior.Strict);
        var controller = CreateController(
            service.Object, nameIdentifier, useDefaultIdentifier: false);

        var result = await controller.Requeue(Guid.NewGuid(), CancellationToken.None);

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
    public void Controller_RequiresGlobalAdminPolicy()
    {
        var attribute = typeof(BookingStatusOutboxAdminController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        attribute.Policy.Should().Be(BusinessPolicies.Admin);
    }

    [Fact]
    public async Task Requeue_MissingRequestIdempotencyKey_ReturnsBadRequestWithoutMutation()
    {
        var service = new Mock<IBookingStatusDeadLetterService>(MockBehavior.Strict);
        var controller = CreateController(service.Object);
        controller.Request.Headers.Remove("Idempotency-Key");

        var result = await controller.Requeue(Guid.NewGuid(), CancellationToken.None);

        var problem = result.Should().BeOfType<ContentResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.Content.Should().Contain(
            "booking_status_requeue_idempotency_key_invalid");
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Requeue_UnknownEvent_ReturnsLocalizedNoStoreProblem()
    {
        var service = new Mock<IBookingStatusDeadLetterService>();
        service.Setup(value => value.RequeueAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeadLetterRequeueResponse?)null);
        var controller = CreateController(service.Object);
        controller.Request.Headers.AcceptLanguage = "he";

        var result = await controller.Requeue(Guid.NewGuid(), CancellationToken.None);

        var problem = result.Should().BeOfType<ContentResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        problem.ContentType.Should().Be("application/problem+json");
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        JsonDocument.Parse(problem.Content!).RootElement.GetProperty("detail")
            .GetString().Should().Contain("לא");
    }

    [Fact]
    public async Task Requeue_DeliveredAfterExplicitRequeue_ReturnsStableSuccess()
    {
        var eventId = Guid.NewGuid();
        var service = new Mock<IBookingStatusDeadLetterService>();
        service.Setup(value => value.RequeueAsync(
                It.IsAny<Guid>(), eventId, It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeadLetterRequeueResponse(
                eventId,
                DeadLetterRequeueOutcomes.AlreadyRequeued,
                "Delivered",
                1));
        var controller = CreateController(service.Object);

        var result = await controller.Requeue(eventId, CancellationToken.None);

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.StatusCode.Should().Be(StatusCodes.Status200OK);
        content.Content.Should().Contain(DeadLetterRequeueOutcomes.AlreadyRequeued);
    }

    private static BookingStatusOutboxAdminController CreateController(
        IBookingStatusDeadLetterService service,
        string? nameIdentifier = null,
        bool useDefaultIdentifier = true)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, BusinessRoles.Admin)
        };
        var identifier = useDefaultIdentifier && nameIdentifier is null
            ? Guid.NewGuid().ToString()
            : nameIdentifier;
        if (identifier is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, identifier));
        }
        var controller = new BookingStatusOutboxAdminController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    claims, "TestAuth")),
                Response = { Body = new MemoryStream() }
            }
        };
        controller.Request.Headers["Idempotency-Key"] = "controller-test-request";
        return controller;
    }
}
