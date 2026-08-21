using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Catalog;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Validators.Catalog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Tests the customer catalog HTTP endpoint contract.
/// </summary>
public class CatalogControllerTests
{
    private readonly Mock<ICatalogReadModelService> _service = new();
    private readonly Mock<IAppLogger> _logger = new();

    [Fact]
    public async Task GetBusinesses_WhenRequestIsValid_ReturnsOkAndNoStoreHeader()
    {
        var expected = new CatalogBusinessesResponse
        {
            Language = "ar",
            Businesses =
            [
                new CatalogBusinessResponse
                {
                    Id = Guid.NewGuid(),
                    SourceId = Guid.NewGuid(),
                    Name = "مغسلة"
                }
            ]
        };
        _service.Setup(service => service.GetBusinessesAsync(
                It.Is<GetCatalogBusinessesRequest>(request =>
                    request.Language == "ar" &&
                    request.Refresh),
                "he",
                default))
            .ReturnsAsync(expected);
        var controller = CreateController();

        var result = await controller.GetBusinesses(
            "ar",
            branchId: null,
            categoryId: null,
            refresh: true,
            acceptLanguage: "he",
            cancellationToken: default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeSameAs(expected);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task GetBusinesses_WhenLanguageOverrideIsInvalid_ReturnsLocalizedProblem()
    {
        var controller = CreateController();

        var result = await controller.GetBusinesses(
            "en",
            branchId: null,
            categoryId: null,
            refresh: false,
            acceptLanguage: "he",
            cancellationToken: default);

        var badRequest = result.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(ConfigurationProblemCodes.LanguageInvalid);
        problem.Extensions["language"].Should().Be(ConfigurationLanguageResolver.Hebrew);
        problem.Extensions["correlationId"].Should().Be("corr-step9-controller");
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetBusiness_WhenServiceThrowsNotFound_ReturnsStableProblem()
    {
        _service.Setup(service => service.GetBusinessAsync(
                It.IsAny<Guid>(),
                It.Is<GetCatalogResourceRequest>(request => request.Language == "ar"),
                "ar",
                default))
            .ThrowsAsync(new CatalogReadModelException(
                CatalogProblemCodes.BusinessNotFound,
                StatusCodes.Status404NotFound,
                "Missing business."));
        var controller = CreateController();

        var result = await controller.GetBusiness(
            Guid.NewGuid(),
            "ar",
            refresh: false,
            acceptLanguage: "ar",
            cancellationToken: default);

        var notFound = result.Should().BeOfType<ObjectResult>().Subject;
        notFound.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        var problem = notFound.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(CatalogProblemCodes.BusinessNotFound);
        problem.Extensions["language"].Should().Be(ConfigurationLanguageResolver.Arabic);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    private CatalogController CreateController()
    {
        return new CatalogController(
            _service.Object,
            new GetCatalogCategoriesRequestValidator(),
            new GetCatalogBusinessesRequestValidator(),
            new GetCatalogBusinessOfferingsRequestValidator(),
            new GetCatalogResourceRequestValidator(),
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "corr-step9-controller"
                }
            }
        };
    }
}
