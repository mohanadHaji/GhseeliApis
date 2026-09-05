using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Checkout;
using GhseeliApis.Middleware;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Validators.Checkout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Moq;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Tests the anonymous checkout draft HTTP contract.
/// </summary>
public class CheckoutDraftsControllerTests
{
    private readonly Mock<ICheckoutDraftService> _service = new();
    private readonly Mock<IAppLogger> _logger = new();

    [Fact]
    public async Task Create_WhenValid_ReturnsOkNoStore_AndUsesDeviceIdentity()
    {
        var deviceId = Guid.NewGuid();
        var expected = new CheckoutDraftResponse
        {
            Language = "ar",
            OrderGuid = Guid.NewGuid(),
            Version = 1,
            RequiresReprice = true
        };
        _service.Setup(service => service.CreateAsync(
                It.Is<CreateCheckoutDraftRequest>(request => request.BusinessSourceId != Guid.Empty),
                deviceId,
                "ar",
                "he",
                default))
            .ReturnsAsync(expected);
        var controller = CreateController(deviceId);
        var request = CreateRequest();

        var result = await controller.Create(
            request,
            language: "ar",
            acceptLanguage: "he",
            cancellationToken: default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(expected);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task Create_WhenLanguageInvalid_ReturnsLocalizedProblem()
    {
        var controller = CreateController(Guid.NewGuid());

        var result = await controller.Create(
            CreateRequest(),
            language: "en",
            acceptLanguage: "he",
            cancellationToken: default);

        var badRequest = result.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(ConfigurationProblemCodes.LanguageInvalid);
        problem.Extensions["language"].Should().Be(ConfigurationLanguageResolver.Hebrew);
        problem.Extensions["correlationId"].Should().Be("corr-step10-controller");
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Update_WhenServiceReturnsVersionConflict_MapsStableProblem()
    {
        var orderGuid = Guid.NewGuid();
        _service.Setup(service => service.UpdateAsync(
                orderGuid,
                It.IsAny<UpdateCheckoutDraftRequest>(),
                It.IsAny<Guid>(),
                "he",
                "ar",
                default))
            .ThrowsAsync(new CheckoutDraftException(
                CheckoutDraftProblemCodes.VersionConflict,
                StatusCodes.Status409Conflict,
                "Conflict.",
                new Dictionary<string, string[]>
                {
                    ["expectedVersion"] = [CheckoutDraftProblemCodes.VersionConflict]
                }));
        var controller = CreateController(Guid.NewGuid());

        var result = await controller.Update(
            orderGuid,
            new UpdateCheckoutDraftRequest
            {
                ExpectedVersion = 1,
                BusinessSourceId = Guid.NewGuid(),
                BranchSourceId = Guid.NewGuid(),
                RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddHours(2),
                Vehicle = new CheckoutDraftVehicleRequest
                {
                    VehicleType = "SUV"
                },
                Location = new CheckoutDraftLocationRequest
                {
                    AddressLine = "Main",
                    Latitude = 32.1,
                    Longitude = 34.8
                },
                Items =
                [
                    new CheckoutDraftItemRequest
                    {
                        OfferingSourceId = Guid.NewGuid()
                    }
                ]
            },
            language: "he",
            acceptLanguage: "ar",
            cancellationToken: default);

        var conflict = result.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(CheckoutDraftProblemCodes.VersionConflict);
        problem.Extensions["language"].Should().Be(ConfigurationLanguageResolver.Hebrew);
        ((Dictionary<string, string[]>)problem.Extensions["fieldErrors"]!)
            .Should()
            .ContainKey("expectedVersion");
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public void CreateAndUpdate_DeclareExplicitRequestBodySizeLimit()
    {
        const long expectedLimit = 65_536;
        var createAttribute = typeof(CheckoutDraftsController)
            .GetMethod(nameof(CheckoutDraftsController.Create))!
            .GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: true)
            .Cast<RequestSizeLimitAttribute>()
            .Single();
        var updateAttribute = typeof(CheckoutDraftsController)
            .GetMethod(nameof(CheckoutDraftsController.Update))!
            .GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: true)
            .Cast<RequestSizeLimitAttribute>()
            .Single();

        ((IRequestSizeLimitMetadata)createAttribute).MaxRequestBodySize.Should().Be(expectedLimit);
        ((IRequestSizeLimitMetadata)updateAttribute).MaxRequestBodySize.Should().Be(expectedLimit);
        createAttribute.Should().BeAssignableTo<IOrderedFilter>();
        updateAttribute.Should().BeAssignableTo<IOrderedFilter>();
    }

    private CheckoutDraftsController CreateController(Guid deviceId)
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "corr-step10-controller"
        };
        httpContext.Items["Ghseeli.DeviceId"] = deviceId;
        httpContext.Items["Ghseeli.InstallationId"] = Guid.NewGuid();

        return new CheckoutDraftsController(
            _service.Object,
            new GetCheckoutDraftRequestValidator(),
            new CreateCheckoutDraftRequestValidator(),
            new UpdateCheckoutDraftRequestValidator(),
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static CreateCheckoutDraftRequest CreateRequest() =>
        new()
        {
            BusinessSourceId = Guid.NewGuid(),
            BranchSourceId = Guid.NewGuid(),
            RequestedSlotStartUtc = DateTimeOffset.UtcNow.AddHours(2),
            Vehicle = new CheckoutDraftVehicleRequest
            {
                VehicleType = "Sedan"
            },
            Location = new CheckoutDraftLocationRequest
            {
                AddressLine = "الشارع 1",
                Latitude = 32.1,
                Longitude = 34.8
            },
            Items =
            [
                new CheckoutDraftItemRequest
                {
                    OfferingSourceId = Guid.NewGuid()
                }
            ]
        };
}
