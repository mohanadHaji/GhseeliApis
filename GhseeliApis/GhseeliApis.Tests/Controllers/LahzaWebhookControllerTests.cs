using System.Text;
using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Controllers;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Tests the bounded, verified Lahza webhook HTTP boundary.
/// </summary>
public sealed class LahzaWebhookControllerTests
{
    private readonly Mock<IPaymentWebhookParser> _parser = new();
    private readonly Mock<IPaymentWebhookService> _service = new();
    private readonly Mock<IOptionsMonitor<LahzaConfigurationOptions>> _options = new();
    private readonly Mock<IAppLogger> _logger = new();

    [Fact]
    public async Task Rejects_non_json_before_signature_parsing()
    {
        var controller = CreateController("text/plain", "{}");
        var result = await controller.HandleWebhook();

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(415);
        _parser.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rejects_streamed_body_over_limit()
    {
        var controller = CreateController(
            "application/json",
            new string('x', (int)LahzaWebhookController.MaxBodyBytes + 1),
            contentLength: null);

        var result = await controller.HandleWebhook();

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(413);
        _parser.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Invalid_signature_returns_stable_problem()
    {
        _parser.Setup(value => value.Parse(
                It.Is<ReadOnlyMemory<byte>>(body =>
                    body.ToArray().SequenceEqual(Encoding.UTF8.GetBytes("{}"))),
                "bad",
                "sk_test_webhook"))
            .Throws(new CustomerPaymentException(
                400,
                CustomerPaymentErrorCodes.SignatureInvalid));
        var controller = CreateController("application/json", "{}");
        controller.Request.Headers["X-Lahza-Signature"] = "bad";

        var result = await controller.HandleWebhook();

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
        ((ProblemDetails)problem.Value!).Extensions["code"]
            .Should().Be(CustomerPaymentErrorCodes.SignatureInvalid);
    }

    [Fact]
    public async Task Missing_signature_returns_distinct_stable_problem()
    {
        var controller = CreateController("application/json", "{}");

        var result = await controller.HandleWebhook();

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
        ((ProblemDetails)problem.Value!).Extensions["code"]
            .Should().Be(CustomerPaymentErrorCodes.SignatureMissing);
        _parser.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Signed_malformed_event_returns_event_invalid()
    {
        _parser.Setup(value => value.Parse(
                It.Is<ReadOnlyMemory<byte>>(body =>
                    body.ToArray().SequenceEqual(Encoding.UTF8.GetBytes("not-json"))),
                "valid",
                "sk_test_webhook"))
            .Throws(new CustomerPaymentException(
                400,
                CustomerPaymentErrorCodes.EventInvalid));
        var controller = CreateController("application/json", "not-json", contentLength: 8);
        controller.Request.Headers["X-Lahza-Signature"] = "valid";

        var result = await controller.HandleWebhook();

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(400);
        ((ProblemDetails)problem.Value!).Extensions["code"]
            .Should().Be(CustomerPaymentErrorCodes.EventInvalid);
    }

    [Fact]
    public async Task Processing_failure_is_retryable_not_acknowledged()
    {
        var verified = new VerifiedPaymentEvent(
            "evt_1", "charge.success", PaymentEventKind.Succeeded,
            "GHSEELI-TEST", "1001", 100, "ILS");
        _parser.Setup(value => value.Parse(
                It.Is<ReadOnlyMemory<byte>>(body =>
                    body.ToArray().SequenceEqual(Encoding.UTF8.GetBytes("{}"))),
                "valid",
                "sk_test_webhook")).Returns(verified);
        _service.Setup(value => value.ProcessAsync(
                verified,
                It.Is<ReadOnlyMemory<byte>>(body =>
                    body.ToArray().SequenceEqual(Encoding.UTF8.GetBytes("{}"))),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var controller = CreateController("application/json", "{}");
        controller.Request.Headers["X-Lahza-Signature"] = "valid";

        var result = await controller.HandleWebhook();

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
    }

    private LahzaWebhookController CreateController(
        string contentType,
        string body,
        long? contentLength = 2)
    {
        _options.SetupGet(value => value.CurrentValue).Returns(new LahzaConfigurationOptions
        {
            SecretKey = "sk_test_webhook"
        });
        var controller = new LahzaWebhookController(
            _parser.Object,
            _service.Object,
            _options.Object,
            _logger.Object);
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;
        context.Request.ContentLength = contentLength;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }
}
