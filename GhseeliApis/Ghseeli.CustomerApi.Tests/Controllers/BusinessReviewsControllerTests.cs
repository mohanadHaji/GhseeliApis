using System.Security.Claims;
using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Reviews;
using GhseeliApis.Services.Reviews;
using GhseeliApis.Validators.Reviews;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Verifies review HTTP status semantics and trusted customer identity handling.
/// </summary>
public sealed class BusinessReviewsControllerTests
{
    [Fact]
    public async Task Put_returns_created_for_first_review()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var response = new OwnedBusinessReviewResponse { Id = Guid.NewGuid() };
        var service = new Mock<IBusinessReviewService>();
        service.Setup(value => value.PutAsync(
                bookingId, userId, It.IsAny<PutBusinessReviewRequest>(), default))
            .ReturnsAsync((response, true));
        var controller = CreateController(service, userId);

        var result = await controller.Put(
            bookingId,
            new PutBusinessReviewRequest { Rating = 5 },
            null,
            default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Put_returns_ok_for_review_update()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var response = new OwnedBusinessReviewResponse { Id = Guid.NewGuid() };
        var service = new Mock<IBusinessReviewService>();
        service.Setup(value => value.PutAsync(
                bookingId, userId, It.IsAny<PutBusinessReviewRequest>(), default))
            .ReturnsAsync((response, false));
        var controller = CreateController(service, userId);

        var result = await controller.Put(
            bookingId,
            new PutBusinessReviewRequest
            {
                Rating = 4,
                ExpectedRowVersion = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8])
            },
            null,
            default);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Delete_returns_no_content()
    {
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var expectedRowVersion = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]);
        var service = new Mock<IBusinessReviewService>();
        service.Setup(value => value.DeleteAsync(
                bookingId, userId, expectedRowVersion, default))
            .Returns(Task.CompletedTask);
        var controller = CreateController(service, userId);

        var result = await controller.Delete(
            bookingId, expectedRowVersion, null, default);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Get_localizes_non_disclosing_booking_not_found()
    {
        var service = new Mock<IBusinessReviewService>();
        service.Setup(value => value.GetOwnedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ThrowsAsync(new BusinessReviewException(
                404, BusinessReviewProblemCodes.BookingNotFound));
        var controller = CreateController(service, Guid.NewGuid());

        var result = await controller.Get(Guid.NewGuid(), "he", default);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
        var details = (ProblemDetails)problem.Value!;
        details.Extensions["code"].Should().Be(BusinessReviewProblemCodes.BookingNotFound);
        details.Extensions["language"].Should().Be("he");
    }

    private static BookingReviewsController CreateController(
        Mock<IBusinessReviewService> service,
        Guid userId)
    {
        var controller = new BookingReviewsController(
            service.Object,
            new PutBusinessReviewRequestValidator(),
            Mock.Of<IAppLogger>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                        new Claim(ClaimTypes.Role, "User")
                    ],
                    "Test"))
            }
        };
        return controller;
    }
}
