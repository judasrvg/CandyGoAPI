using System.Security.Claims;
using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [AllowAnonymous]
    [HttpGet("vapid-public-key")]
    public async Task<ActionResult<PushPublicKeyDto>> GetVapidPublicKey()
    {
        var dto = await _notificationService.GetPublicKeyAsync();
        return Ok(dto);
    }

    [Authorize(Roles = "client")]
    [HttpPost("subscriptions")]
    public async Task<ActionResult<PushSubscriptionStatusDto>> UpsertSubscription([FromBody] UpsertPushSubscriptionRequest request)
    {
        var clientId = GetCurrentClientId();
        var dto = await _notificationService.UpsertClientSubscriptionAsync(clientId, request);
        return Ok(dto);
    }

    [Authorize(Roles = "client")]
    [HttpDelete("subscriptions")]
    public async Task<IActionResult> RemoveSubscription([FromBody] RemovePushSubscriptionRequest request)
    {
        var clientId = GetCurrentClientId();
        await _notificationService.RemoveClientSubscriptionAsync(clientId, request);
        return NoContent();
    }

    private long GetCurrentClientId()
    {
        var subClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(ClaimTypes.Sid)
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue("sub");

        if (!long.TryParse(subClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Token inválido.");
        }

        return userId;
    }
}
