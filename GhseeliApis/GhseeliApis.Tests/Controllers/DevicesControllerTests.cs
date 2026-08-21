using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Controllers;
using GhseeliApis.DTOs.Devices;
using GhseeliApis.Services.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace GhseeliApis.Tests.Controllers;

/// <summary>
/// Tests the device registration HTTP endpoint.
/// </summary>
public class DevicesControllerTests
{
    private readonly Mock<IDeviceRegistrationService> _service = new();
    private readonly Mock<IAppLogger> _logger = new();

    [Fact]
    public async Task Register_FirstIssuance_ReturnsOkResponse()
    {
        var request = new RegisterDeviceRequest
        {
            InstallationId = Guid.NewGuid(),
            Platform = "iOS"
        };
        var expected = new RegisterDeviceResponse
        {
            DeviceId = Guid.NewGuid(),
            InstallationId = request.InstallationId,
            Platform = "iOS",
            Token = "token",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(90)
        };
        _service.Setup(service => service.RegisterAsync(request, null, default))
            .ReturnsAsync(expected);
        var controller = CreateController();

        var result = await controller.Register(request, default);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Register_Rotation_ForwardsCurrentDeviceToken()
    {
        var request = new RegisterDeviceRequest
        {
            InstallationId = Guid.NewGuid(),
            Platform = "Android"
        };
        _service.Setup(service => service.RegisterAsync(request, "current-token", default))
            .ReturnsAsync(new RegisterDeviceResponse());
        var controller = CreateController();
        controller.Request.Headers[DeviceTokenDefaults.HeaderName] = "current-token";

        await controller.Register(request, default);

        _service.Verify(service => service.RegisterAsync(request, "current-token", default));
    }

    [Fact]
    public async Task Register_ServiceConflict_ReturnsStableProblemDetails()
    {
        var request = new RegisterDeviceRequest
        {
            InstallationId = Guid.NewGuid(),
            Platform = "iOS"
        };
        _service.Setup(service => service.RegisterAsync(request, null, default))
            .ThrowsAsync(new DeviceRegistrationException(
                DeviceProblemCodes.RegistrationConflict,
                StatusCodes.Status409Conflict,
                "Already registered."));
        var controller = CreateController();

        var result = await controller.Register(request, default);

        var conflict = result.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(DeviceProblemCodes.RegistrationConflict);
        problem.Extensions["correlationId"].Should().Be("corr-step7-controller");
    }

    private DevicesController CreateController()
    {
        var controller = new DevicesController(_service.Object, _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "corr-step7-controller"
                }
            }
        };
        return controller;
    }
}
