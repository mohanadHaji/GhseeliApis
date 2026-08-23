using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Checkout;
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
/// Tests the anonymous authoritative pricing HTTP contract.
/// </summary>
public class PricingControllerTests
{
    private readonly Mock<ICheckoutPricingService> _service = new();
    private readonly Mock<IAppLogger> _logger = new();

    [Fact]
    public async Task DirectReprice_WhenValid_ReturnsOkNoStore_AndUsesDeviceIdentity()
    {
        var deviceId = Guid.NewGuid();
        var expected = new DirectCheckoutPricingResponse
        {
            Language = "ar",
            Pricing = new CheckoutPricingSnapshotResponse
            {
                Currency = "ILS",
                GrandTotal = 89m
            }
        };
        _service.Setup(service => service.RepriceAsync(
                It.Is<CreateCheckoutDraftRequest>(request => request.BusinessSourceId != Guid.Empty),
                deviceId,
                "ar",
                "he",
                default))
            .ReturnsAsync(expected);
        var controller = CreatePricingController(deviceId);

        var result = await controller.Reprice(
            CreateDraftRequest(),
            language: "ar",
            acceptLanguage: "he",
            cancellationToken: default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(expected);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task DirectReprice_WhenLanguageInvalid_ReturnsLocalizedProblem()
    {
        var controller = CreatePricingController(Guid.NewGuid());

        var result = await controller.Reprice(
            CreateDraftRequest(),
            language: "en",
            acceptLanguage: "he",
            cancellationToken: default);

        var badRequest = result.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        badRequest.ContentTypes.Should().ContainSingle("application/problem+json");
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(ConfigurationProblemCodes.LanguageInvalid);
        problem.Extensions["language"].Should().Be(ConfigurationLanguageResolver.Hebrew);
        problem.Extensions["correlationId"].Should().Be("corr-step11-pricing-controller");
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DraftReprice_WhenOrderGuidMissing_ReturnsLocalizedProblem()
    {
        var controller = CreateCheckoutPricingController(Guid.NewGuid());

        var result = await controller.Reprice(
            new RepriceCheckoutDraftRequest
            {
                ExpectedVersion = 1
            },
            orderGuid: null,
            language: "he",
            acceptLanguage: "ar",
            cancellationToken: default);

        var badRequest = result.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        badRequest.ContentTypes.Should().ContainSingle("application/problem+json");
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(CheckoutPricingProblemCodes.Invalid);
        problem.Extensions["language"].Should().Be(ConfigurationLanguageResolver.Hebrew);
        ((Dictionary<string, string[]>)problem.Extensions["fieldErrors"]!)
            .Should()
            .ContainKey("orderGuid");
    }

    [Fact]
    public async Task DraftReprice_WhenServiceReturnsVersionConflict_MapsStableProblem()
    {
        var orderGuid = Guid.NewGuid();
        _service.Setup(service => service.RepriceDraftAsync(
                orderGuid,
                It.IsAny<RepriceCheckoutDraftRequest>(),
                It.IsAny<Guid>(),
                "he",
                "ar",
                default))
            .ThrowsAsync(new CheckoutPricingException(
                CheckoutDraftProblemCodes.VersionConflict,
                StatusCodes.Status409Conflict,
                "Conflict.",
                new Dictionary<string, string[]>
                {
                    ["expectedVersion"] = [CheckoutDraftProblemCodes.VersionConflict]
                }));
        var controller = CreateCheckoutPricingController(Guid.NewGuid());

        var result = await controller.Reprice(
            new RepriceCheckoutDraftRequest
            {
                ExpectedVersion = 1
            },
            orderGuid.ToString("D"),
            language: "he",
            acceptLanguage: "ar",
            cancellationToken: default);

        var conflict = result.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        conflict.ContentTypes.Should().ContainSingle("application/problem+json");
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(CheckoutDraftProblemCodes.VersionConflict);
        problem.Extensions["language"].Should().Be(ConfigurationLanguageResolver.Hebrew);
        ((Dictionary<string, string[]>)problem.Extensions["fieldErrors"]!)
            .Should()
            .ContainKey("expectedVersion");
    }

    [Fact]
    public void RepriceEndpoints_DeclareExplicitRequestBodySizeLimit()
    {
        const long expectedLimit = 65_536;
        var directAttribute = typeof(PricingController)
            .GetMethod(nameof(PricingController.Reprice))!
            .GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: true)
            .Cast<RequestSizeLimitAttribute>()
            .Single();
        var draftAttribute = typeof(CheckoutPricingController)
            .GetMethod(nameof(CheckoutPricingController.Reprice))!
            .GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: true)
            .Cast<RequestSizeLimitAttribute>()
            .Single();

        ((IRequestSizeLimitMetadata)directAttribute).MaxRequestBodySize.Should().Be(expectedLimit);
        ((IRequestSizeLimitMetadata)draftAttribute).MaxRequestBodySize.Should().Be(expectedLimit);
        directAttribute.Should().BeAssignableTo<IOrderedFilter>();
        draftAttribute.Should().BeAssignableTo<IOrderedFilter>();
    }

    private PricingController CreatePricingController(Guid deviceId)
    {
        var httpContext = CreateHttpContext(deviceId);

        return new PricingController(
            _service.Object,
            new GetCheckoutDraftRequestValidator(),
            new CreateCheckoutDraftRequestValidator(),
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private CheckoutPricingController CreateCheckoutPricingController(Guid deviceId)
    {
        var httpContext = CreateHttpContext(deviceId);

        return new CheckoutPricingController(
            _service.Object,
            new GetCheckoutDraftRequestValidator(),
            new RepriceCheckoutDraftRequestValidator(),
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static DefaultHttpContext CreateHttpContext(Guid deviceId)
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "corr-step11-pricing-controller"
        };
        httpContext.Items["Ghseeli.DeviceId"] = deviceId;
        httpContext.Items["Ghseeli.InstallationId"] = Guid.NewGuid();
        return httpContext;
    }

    private static CreateCheckoutDraftRequest CreateDraftRequest() =>
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
