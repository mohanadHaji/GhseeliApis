using FluentAssertions;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Controllers;
using Ghseeli.BusinessApi.DTOs.Catalog;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.Common.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace Ghseeli.BusinessApi.Tests.Controllers;

/// <summary>
/// Verifies catalog controller exception handling and warning logs.
/// </summary>
public class CatalogControllerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ICatalogService> _catalogService = new();
    private readonly Mock<IAppLogger> _logger = new();
    private readonly CatalogController _controller;

    public CatalogControllerTests()
    {
        _controller = new CatalogController(_catalogService.Object, _logger.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, _userId.ToString()),
                    new Claim(ClaimTypes.Role, BusinessRoles.Owner)
                ], "TestAuth"))
            }
        };
    }

    [Fact]
    public async Task CreateCategory_WhenValidationFails_ReturnsBadRequestAndLogsWarning()
    {
        _catalogService.Setup(service => service.CreateCategoryAsync(
                _userId,
                false,
                It.IsAny<CreateServiceCategoryRequest>()))
            .ThrowsAsync(CatalogValidationException.ForField("nameAr", "Arabic category name is required."));

        var result = await _controller.CreateCategory(new CreateServiceCategoryRequest());

        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        _logger.Verify(logger => logger.LogWarning(
            It.Is<string>(message =>
                message.Contains("validation failed", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("nameAr", StringComparison.OrdinalIgnoreCase))),
            Times.Once);
    }

    [Fact]
    public async Task GetCategory_WhenAccessIsRejected_ReturnsForbidAndLogsWarning()
    {
        var categoryId = Guid.NewGuid();
        _catalogService.Setup(service => service.GetCategoryAsync(_userId, false, categoryId))
            .ThrowsAsync(new UnauthorizedAccessException("Denied"));

        var result = await _controller.GetCategory(categoryId);

        result.Should().BeOfType<ForbidResult>();
        _logger.Verify(logger => logger.LogWarning(
            It.Is<string>(message =>
                message.Contains("access denied", StringComparison.OrdinalIgnoreCase) &&
                message.Contains(_userId.ToString(), StringComparison.OrdinalIgnoreCase))),
            Times.Once);
    }

    [Fact]
    public async Task GetCategory_WhenResourceIsMissing_ReturnsNotFoundAndLogsWarning()
    {
        var categoryId = Guid.NewGuid();
        _catalogService.Setup(service => service.GetCategoryAsync(_userId, false, categoryId))
            .ThrowsAsync(new KeyNotFoundException("The category was not found."));

        var result = await _controller.GetCategory(categoryId);

        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        _logger.Verify(logger => logger.LogWarning(
            It.Is<string>(message =>
                message.Contains("not found", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("category", StringComparison.OrdinalIgnoreCase))),
            Times.Once);
    }
}
