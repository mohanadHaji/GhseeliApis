using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Devices;
using GhseeliApis.Middleware;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GhseeliApis.Controllers;

[ApiController]
[Route("api/v1/devices")]
[AllowAnonymous]
public sealed class DevicesController : ControllerBase
{
    private readonly IDeviceRegistrationService _deviceService;
    private readonly IAppLogger _logger;

    public DevicesController(
        IDeviceRegistrationService deviceService,
        IAppLogger logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowWithoutDeviceToken]
    [EnableRateLimiting(CustomerRateLimitPolicyNames.DeviceRegistration)]
    [ProducesResponseType<RegisterDeviceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var currentToken = Request.Headers[DeviceTokenDefaults.HeaderName].ToString();
        try
        {
            var response = await _deviceService.RegisterAsync(
                request,
                string.IsNullOrWhiteSpace(currentToken) ? null : currentToken,
                cancellationToken);
            return Ok(response);
        }
        catch (DeviceRegistrationException exception)
        {
            _logger.LogWarning(
                $"Device registration rejected for installation {request.InstallationId} with code {exception.Code}.");
            var problem = new ProblemDetails
            {
                Status = exception.StatusCode,
                Title = "Device registration failed.",
                Detail = exception.Message,
                Type = $"https://api.ghseeli.example/errors/{exception.Code}"
            };
            problem.Extensions["code"] = exception.Code;
            problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;
            problem.Extensions["language"] = ConfigurationLanguageResolver.ResolveFromHeader(
                Request.Headers.AcceptLanguage.ToString());
            return new ObjectResult(problem)
            {
                StatusCode = exception.StatusCode,
                ContentTypes = { "application/problem+json" }
            };
        }
    }
}
