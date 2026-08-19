using Ghseeli.BusinessApi.DTOs.Auth;
using Ghseeli.BusinessApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Ghseeli.BusinessApi.Controllers;

[ApiController]
[Route("api/v1/business/auth")]
public class BusinessAuthController : ControllerBase
{
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
            return Ok(await _authService.RegisterOwnerAsync(request));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { Message = exception.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(BusinessLoginRequest request)
    {
        try
        {
            return Ok(await _authService.LoginAsync(request));
        }
        catch (InvalidOperationException exception)
        {
            return Unauthorized(new { Message = exception.Message });
        }
    }
}
