using CandyGo.Api.DTOs;
using CandyGo.Api.Security;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CandyGo.Api.Controllers;

[ApiController]
[EnableRateLimiting("AuthEndpoints")]
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
        ApplyNoStoreHeaders();
        SetAuthCookie(AuthCookieNames.ClientAccessToken, response);
        return Ok(response);
    }

    [HttpPost("client/login")]
    public async Task<ActionResult<AuthResponse>> LoginClient([FromBody] ClientLoginRequest request)
    {
        var response = await _authService.LoginClientAsync(request);
        ApplyNoStoreHeaders();
        SetAuthCookie(AuthCookieNames.ClientAccessToken, response);
        return Ok(response);
    }

    [HttpPost("client/logout")]
    public IActionResult LogoutClient()
    {
        ApplyNoStoreHeaders();
        ClearAuthCookie(AuthCookieNames.ClientAccessToken);
        return NoContent();
    }

    [HttpPost("admin/login")]
    public async Task<ActionResult<AuthResponse>> LoginAdmin([FromBody] AdminLoginRequest request)
    {
        var response = await _authService.LoginAdminAsync(request);
        ApplyNoStoreHeaders();
        SetAuthCookie(AuthCookieNames.AdminAccessToken, response);
        return Ok(response);
    }

    [HttpPost("admin/logout")]
    public IActionResult LogoutAdmin()
    {
        ApplyNoStoreHeaders();
        ClearAuthCookie(AuthCookieNames.AdminAccessToken);
        return NoContent();
    }

    [HttpPost("admin/bootstrap")]
    public async Task<ActionResult<AuthResponse>> BootstrapAdmin([FromBody] BootstrapAdminRequest request)
    {
        var bootstrapKey = Request.Headers["X-Bootstrap-Key"].ToString();
        var response = await _authService.BootstrapAdminAsync(request, bootstrapKey);
        ApplyNoStoreHeaders();
        SetAuthCookie(AuthCookieNames.AdminAccessToken, response);
        return Ok(response);
    }

    private void SetAuthCookie(string cookieName, AuthResponse response)
    {
        var isHttps = IsHttpsRequest();
        var maxAge = response.ExpiresAtUtc <= DateTime.UtcNow
            ? TimeSpan.FromMinutes(1)
            : response.ExpiresAtUtc - DateTime.UtcNow;

        Response.Cookies.Append(cookieName, response.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = "/",
            Expires = response.ExpiresAtUtc,
            MaxAge = maxAge
        });
    }

    private void ClearAuthCookie(string cookieName)
    {
        var isHttps = IsHttpsRequest();

        Response.Cookies.Delete(cookieName, new CookieOptions
        {
            Path = "/",
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            HttpOnly = true
        });
    }

    private void ApplyNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }

    private bool IsHttpsRequest()
    {
        if (Request.IsHttps)
        {
            return true;
        }

        var forwardedProto = Request.Headers["X-Forwarded-Proto"].ToString();
        return string.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase);
    }
}
