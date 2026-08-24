using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Payment;
using GhseeliApis.Services.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Tests customer payment routing and identity handling.
/// </summary>
public sealed class CustomerPaymentsControllerTests
{
    [Fact]
    public async Task Create_safely_rejects_malformed_claim()
    {
        var service = new Mock<ICustomerPaymentService>();
        var controller = CreateController(service, "not-a-guid", Guid.NewGuid());
        controller.Request.Headers["Idempotency-Key"] = "key";

        var result = await controller.Create(
            new CreateCustomerPaymentIntentRequest
            {
                BookingId = Guid.NewGuid(),
                Method = "Card"
            },
            "key",
            null,
            default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(401);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_returns_same_not_found_for_unowned_payment()
    {
        var service = new Mock<ICustomerPaymentService>();
        service.Setup(value => value.GetAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync((CustomerPaymentResponse?)null);
        var controller = CreateController(
            service, Guid.NewGuid().ToString("D"), Guid.NewGuid());

        var result = await controller.Get(Guid.NewGuid(), null, default);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
        ((ProblemDetails)problem.Value!).Extensions["code"]
            .Should().Be(CustomerPaymentErrorCodes.NotFound);
    }

    [Fact]
    public async Task Create_and_replay_success_return_200()
    {
        var service = new Mock<ICustomerPaymentService>();
        var response = new CustomerPaymentResponse
        {
            Id = Guid.NewGuid(),
            BookingId = Guid.NewGuid(),
            Status = "Pending"
        };
        service.Setup(value => value.CreateAsync(
                It.IsAny<CreateCustomerPaymentIntentRequest>(),
                "same-key",
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                default))
            .ReturnsAsync(response);
        var controller = CreateController(
            service, Guid.NewGuid().ToString("D"), Guid.NewGuid());
        controller.Request.Headers["Idempotency-Key"] = "same-key";

        var result = await controller.Create(
            new CreateCustomerPaymentIntentRequest
            {
                BookingId = response.BookingId,
                Method = "Card"
            },
            "same-key",
            null,
            default);

        result.Should().BeOfType<OkObjectResult>().Which.StatusCode.Should().Be(200);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Crypto")]
    public async Task Create_rejects_missing_blank_or_unknown_method(string? method)
    {
        var service = new Mock<ICustomerPaymentService>();
        var controller = CreateController(
            service, Guid.NewGuid().ToString("D"), Guid.NewGuid());

        var result = await controller.Create(
            new CreateCustomerPaymentIntentRequest
            {
                BookingId = Guid.NewGuid(),
                Method = method!
            },
            "valid-key",
            null,
            default);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
        ((ProblemDetails)problem.Value!).Extensions["code"]
            .Should().Be(CustomerPaymentErrorCodes.Invalid);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_missing_booking_id_returns_field_error()
    {
        var service = new Mock<ICustomerPaymentService>();
        var controller = CreateController(
            service, Guid.NewGuid().ToString("D"), Guid.NewGuid());

        var result = await controller.Create(
            new CreateCustomerPaymentIntentRequest
            {
                Method = "Card"
            },
            "valid-key",
            null,
            default);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
        var details = (ProblemDetails)problem.Value!;
        details.Extensions["code"].Should().Be(CustomerPaymentErrorCodes.Invalid);
        details.Extensions["fieldErrors"].Should().BeEquivalentTo(
            new Dictionary<string, string[]>
            {
                ["bookingId"] = ["Booking ID is required."]
            });
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public void Idempotency_conflict_code_matches_public_contract()
    {
        CustomerPaymentErrorCodes.IdempotencyConflict
            .Should().Be("idempotency_conflict");
    }

    [Theory]
    [InlineData("Wallet")]
    [InlineData("CashOnArrival")]
    [InlineData("ThirdParty")]
    public async Task Create_rejects_known_disabled_method_as_conflict(string method)
    {
        var service = new Mock<ICustomerPaymentService>();
        var controller = CreateController(
            service, Guid.NewGuid().ToString("D"), Guid.NewGuid());

        var result = await controller.Create(
            new CreateCustomerPaymentIntentRequest
            {
                BookingId = Guid.NewGuid(),
                Method = method
            },
            "valid-key",
            null,
            default);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(409);
        ((ProblemDetails)problem.Value!).Extensions["code"]
            .Should().Be(CustomerPaymentErrorCodes.MethodNotYetSupported);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_requires_idempotency_key()
    {
        var service = new Mock<ICustomerPaymentService>();
        var controller = CreateController(
            service, Guid.NewGuid().ToString("D"), Guid.NewGuid());

        var result = await controller.Create(
            new CreateCustomerPaymentIntentRequest
            {
                BookingId = Guid.NewGuid(),
                Method = "Card"
            },
            null,
            null,
            default);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
        ((ProblemDetails)problem.Value!).Extensions["code"]
            .Should().Be(CustomerPaymentErrorCodes.IdempotencyKeyRequired);
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_empty_idempotency_key(string key)
    {
        var service = new Mock<ICustomerPaymentService>();
        var controller = CreateController(
            service, Guid.NewGuid().ToString("D"), Guid.NewGuid());

        var result = await controller.Create(
            new CreateCustomerPaymentIntentRequest
            {
                BookingId = Guid.NewGuid(),
                Method = "Card"
            },
            key,
            null,
            default);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
        ((ProblemDetails)problem.Value!).Extensions["code"]
            .Should().Be(CustomerPaymentErrorCodes.IdempotencyKeyInvalid);
        service.VerifyNoOtherCalls();
    }

    private static CustomerPaymentsController CreateController(
        Mock<ICustomerPaymentService> service,
        string userId,
        Guid deviceId)
    {
        var controller = new CustomerPaymentsController(
            service.Object,
            Mock.Of<IAppLogger>());
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                "Test"))
        };
        context.Items["Ghseeli.DeviceId"] = deviceId;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }
}
