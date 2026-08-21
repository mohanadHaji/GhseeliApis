using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Middleware;
using GhseeliApis.Services.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Moq;
using System.Text.Json;

namespace GhseeliApis.Tests.Middleware;

/// <summary>
/// Tests device-token enforcement for versioned Customer API routes.
/// </summary>
public class DeviceTokenMiddlewareTests
{
    private readonly Mock<IDeviceRegistrationService> _service = new();
    private readonly Mock<IAppLogger> _logger = new();

    [Fact]
    public async Task InvokeAsync_LegacyRoute_IsNotProtected()
    {
        var called = false;
        var context = CreateContext("/api/auth/login");
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, _service.Object);

        called.Should().BeTrue();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_ExplicitlyExemptEndpoint_IsNotProtected()
    {
        var called = false;
        var context = CreateContext("/api/v1/devices/register");
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AllowWithoutDeviceTokenAttribute()),
            "register"));
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, _service.Object);

        called.Should().BeTrue();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_ProtectedRouteWithoutHeader_ReturnsProblem()
    {
        var context = CreateProtectedContext();
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _service.Object);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        document.RootElement.GetProperty("code").GetString().Should().Be(DeviceProblemCodes.TokenMissing);
    }

    [Fact]
    public async Task InvokeAsync_ValidToken_AttachesIdentityAndContinues()
    {
        var called = false;
        var deviceId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var context = CreateProtectedContext();
        context.Request.Headers[DeviceTokenDefaults.HeaderName] =
            Token(9);
        _service.Setup(service => service.AuthenticateAsync(It.IsAny<string>(), default))
            .ReturnsAsync(DeviceAuthenticationResult.Success(deviceId, installationId));
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, _service.Object);

        called.Should().BeTrue();
        context.GetDeviceId().Should().Be(deviceId);
        context.GetInstallationId().Should().Be(installationId);
        _service.Verify(service => service.UpdateLastSeenAsync(deviceId, default), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_UnknownToken_ReturnsStableProblem()
    {
        var context = CreateProtectedContext();
        context.Request.Headers[DeviceTokenDefaults.HeaderName] =
            Token(10);
        _service.Setup(service => service.AuthenticateAsync(It.IsAny<string>(), default))
            .ReturnsAsync(DeviceAuthenticationResult.Failure(DeviceProblemCodes.TokenInvalid));
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _service.Object);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    private DeviceTokenMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, _logger.Object);

    private static DefaultHttpContext CreateProtectedContext()
    {
        var context = CreateContext("/api/v1/configuration");
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "protected"));
        return context;
    }

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "corr-step7";
        return context;
    }

    private static string Token(byte value) =>
        WebEncoders.Base64UrlEncode(Enumerable.Repeat(value, 32).ToArray());
}
