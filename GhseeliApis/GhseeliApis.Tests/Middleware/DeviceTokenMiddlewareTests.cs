using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.Middleware;
using GhseeliApis.Services.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
    public async Task InvokeAsync_ProtectedRouteWithoutHeader_UsesValidQueryOverrideForLocalization()
    {
        var context = CreateProtectedContext(queryString: "?language=he");
        context.Request.Headers["Accept-Language"] = "ar";
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _service.Object);

        using var document = await ReadResponseBodyAsJsonAsync(context);
        AssertProblem(
            document.RootElement,
            DeviceProblemCodes.TokenMissing,
            "he",
            "אימות המכשיר נכשל.",
            "נדרש אסימון מכשיר.");
    }

    [Fact]
    public async Task InvokeAsync_InvalidQueryOverride_FallsBackToSupportedHeaderLanguage()
    {
        var context = CreateProtectedContext(queryString: "?language=en");
        context.Request.Headers["Accept-Language"] = "-, ;q=1, he-IL;q=0.8";
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _service.Object);

        using var document = await ReadResponseBodyAsJsonAsync(context);
        AssertProblem(
            document.RootElement,
            DeviceProblemCodes.TokenMissing,
            "ar",
            "فشل التحقق من الجهاز.",
            "رمز الجهاز مطلوب.");
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
    public async Task InvokeAsync_ExpiredTokenWithMalformedHeader_FallsBackToArabicProblem()
    {
        var context = CreateProtectedContext();
        context.Request.Headers[DeviceTokenDefaults.HeaderName] = Token(10);
        context.Request.Headers["Accept-Language"] = "-, ;q=1";
        _service.Setup(service => service.AuthenticateAsync(It.IsAny<string>(), default))
            .ReturnsAsync(DeviceAuthenticationResult.Failure(DeviceProblemCodes.TokenExpired));
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _service.Object);

        using var document = await ReadResponseBodyAsJsonAsync(context);
        AssertProblem(
            document.RootElement,
            DeviceProblemCodes.TokenExpired,
            "ar",
            "فشل التحقق من الجهاز.",
            "انتهت صلاحية رمز الجهاز.");
    }

    [Fact]
    public async Task InvokeAsync_UnknownToken_ReturnsLocalizedStableProblem()
    {
        var context = CreateProtectedContext();
        context.Request.Headers["Accept-Language"] = "he";
        context.Request.Headers[DeviceTokenDefaults.HeaderName] = Token(11);
        _service.Setup(service => service.AuthenticateAsync(It.IsAny<string>(), default))
            .ReturnsAsync(DeviceAuthenticationResult.Failure(DeviceProblemCodes.TokenInvalid));
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _service.Object);

        using var document = await ReadResponseBodyAsJsonAsync(context);
        AssertProblem(
            document.RootElement,
            DeviceProblemCodes.TokenInvalid,
            "he",
            "אימות המכשיר נכשל.",
            "אסימון המכשיר אינו תקין.");
    }

    private DeviceTokenMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, _logger.Object);

    private static DefaultHttpContext CreateProtectedContext(string? queryString = null)
    {
        var context = CreateContext("/api/v1/configuration", queryString);
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "protected"));
        return context;
    }

    private static DefaultHttpContext CreateContext(string path, string? queryString = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (!string.IsNullOrWhiteSpace(queryString))
        {
            context.Request.QueryString = new QueryString(queryString);
        }

        var responseBody = new MemoryStream();
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(responseBody));
        context.Response.Body = responseBody;
        context.TraceIdentifier = "corr-step7";
        return context;
    }

    private static async Task<JsonDocument> ReadResponseBodyAsJsonAsync(DefaultHttpContext context)
    {
        await context.Response.CompleteAsync();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    private static void AssertProblem(
        JsonElement root,
        string expectedCode,
        string expectedLanguage,
        string expectedTitle,
        string expectedDetail)
    {
        root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status401Unauthorized);
        root.GetProperty("code").GetString().Should().Be(expectedCode);
        root.GetProperty("language").GetString().Should().Be(expectedLanguage);
        root.GetProperty("title").GetString().Should().Be(expectedTitle);
        root.GetProperty("detail").GetString().Should().Be(expectedDetail);
        root.GetProperty("correlationId").GetString().Should().Be("corr-step7");
    }

    private static string Token(byte value) =>
        WebEncoders.Base64UrlEncode(Enumerable.Repeat(value, 32).ToArray());
}
