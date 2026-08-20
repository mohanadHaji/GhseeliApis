using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Controllers;
using Ghseeli.BusinessApi.DTOs.Companies;
using Ghseeli.BusinessApi.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace Ghseeli.BusinessApi.Tests.Controllers;

/// <summary>
/// Verifies company profile controller exception mapping for assignment-scoped routes.
/// </summary>
public class CompanyProfileControllerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ICompanyProfileService> _service = new();
    private readonly CompanyProfileController _controller;

    public CompanyProfileControllerTests()
    {
        _controller = new CompanyProfileController(_service.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, _userId.ToString()),
                    new Claim(ClaimTypes.Role, BusinessRoles.Admin)
                ], "TestAuth"))
            }
        };
    }

    [Fact]
    public async Task GetMyCompany_WhenAccessIsRejected_ReturnsForbid()
    {
        _service.Setup(service => service.GetMyCompanyAsync(_userId))
            .ThrowsAsync(new UnauthorizedAccessException("No active assignment."));

        var result = await _controller.GetMyCompany();

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task CreateBranch_WhenValidationFails_ReturnsBadRequest()
    {
        _service.Setup(service => service.CreateBranchAsync(
                _userId,
                It.IsAny<CreateBranchRequest>()))
            .ThrowsAsync(new ValidationException(
            [
                new ValidationFailure(nameof(CreateBranchRequest.Latitude), "Latitude and longitude must be supplied together.")
            ]));

        var result = await _controller.CreateBranch(new CreateBranchRequest());

        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        contentResult.Content.Should().ContainEquivalentOf("latitude");
    }

    [Fact]
    public async Task UpdateBranch_WhenResourceIsMissing_ReturnsNotFound()
    {
        var branchId = Guid.NewGuid();
        _service.Setup(service => service.UpdateBranchAsync(
                _userId,
                branchId,
                It.IsAny<UpdateBranchRequest>()))
            .ThrowsAsync(new KeyNotFoundException("The branch was not found."));

        var result = await _controller.UpdateBranch(branchId, new UpdateBranchRequest());

        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        contentResult.Content.Should().ContainEquivalentOf("not found");
    }
}
