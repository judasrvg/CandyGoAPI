using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("client/register")]
    public async Task<ActionResult<AuthResponse>> RegisterClient([FromBody] RegisterClientRequest request)
    {
        var response = await _authService.RegisterClientAsync(request);
        return Ok(response);
    }

    [HttpPost("client/login")]
    public async Task<ActionResult<AuthResponse>> LoginClient([FromBody] ClientLoginRequest request)
    {
        var response = await _authService.LoginClientAsync(request);
        return Ok(response);
    }

    [HttpPost("admin/login")]
    public async Task<ActionResult<AuthResponse>> LoginAdmin([FromBody] AdminLoginRequest request)
    {
        var response = await _authService.LoginAdminAsync(request);
        return Ok(response);
    }

    [HttpPost("admin/bootstrap")]
    public async Task<ActionResult<AuthResponse>> BootstrapAdmin([FromBody] BootstrapAdminRequest request)
    {
        var bootstrapKey = Request.Headers["X-Bootstrap-Key"].ToString();
        var response = await _authService.BootstrapAdminAsync(request, bootstrapKey);
        return Ok(response);
    }
}
