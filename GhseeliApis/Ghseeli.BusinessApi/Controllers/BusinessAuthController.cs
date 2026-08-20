using Ghseeli.BusinessApi.DTOs.Auth;
using Ghseeli.BusinessApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/business/auth")]
public class BusinessAuthController : ControllerBase
{
    private static readonly JsonSerializerOptions ResponseJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IBusinessAuthService _authService;

    public BusinessAuthController(IBusinessAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register-owner")]
    public async Task<IActionResult> RegisterOwner(RegisterOwnerRequest request)
    {
        try
        {
            return JsonResponse(StatusCodes.Status200OK, await _authService.RegisterOwnerAsync(request));
        }
        catch (InvalidOperationException exception)
        {
            return JsonResponse(StatusCodes.Status400BadRequest, new { Message = exception.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(BusinessLoginRequest request)
    {
        try
        {
            return JsonResponse(StatusCodes.Status200OK, await _authService.LoginAsync(request));
        }
        catch (InvalidOperationException exception)
        {
            return JsonResponse(StatusCodes.Status401Unauthorized, new { Message = exception.Message });
        }
    }

    private static ContentResult JsonResponse(int statusCode, object value)
    {
        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(value, ResponseJsonOptions)
        };
    }
}
