using System.Security.Claims;
using CandyGo.Api.DTOs;
using CandyGo.Api.Security;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CandyGo.Api.Controllers;

[ApiController]
[Route("api/sync")]
public sealed class SyncController : ControllerBase
{
    private readonly ISyncService _syncService;
    private readonly SyncOptions _syncOptions;

    public SyncController(ISyncService syncService, IOptions<SyncOptions> syncOptions)
    {
        _syncService = syncService;
        _syncOptions = syncOptions.Value;
    }

    [HttpGet("orders")]
    public async Task<ActionResult<SyncOrdersResponse>> PullOrders(
        [FromQuery] DateTime? updatedAfterUtc,
        [FromQuery] long? lastOrderId,
        [FromQuery] int take = 200)
    {
        EnsureSyncAuthorized();

        var data = await _syncService.PullOrdersAsync(updatedAfterUtc, lastOrderId, take);
        return Ok(data);
    }

    [HttpGet("clients")]
    public async Task<ActionResult<SyncClientsResponse>> PullClients([FromQuery] DateTime? updatedAfterUtc, [FromQuery] int take = 200)
    {
        EnsureSyncAuthorized();

        var data = await _syncService.PullClientsAsync(updatedAfterUtc, take);
        return Ok(data);
    }

    [HttpPost("orders/status")]
    public async Task<ActionResult<SyncOrderStatusBatchResponse>> PushOrderStatuses([FromBody] SyncOrderStatusBatchRequest request)
    {
        EnsureSyncAuthorized();

        var changedBy = GetChangedBy();
        var result = await _syncService.PushOrderStatusesAsync(request, changedBy);
        return Ok(result);
    }

    [HttpPost("rewards/reservations")]
    public async Task<ActionResult<SyncReservationRewardBatchResponse>> PushReservationRewards([FromBody] SyncReservationRewardBatchRequest request)
    {
        EnsureSyncAuthorized();

        var changedBy = GetChangedBy();
        var result = await _syncService.ApplyReservationRewardsAsync(request, changedBy);
        return Ok(result);
    }

    private void EnsureSyncAuthorized()
    {
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("admin"))
        {
            return;
        }

        var key = Request.Headers["X-Sync-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(_syncOptions.ApiKey) && string.Equals(key, _syncOptions.ApiKey, StringComparison.Ordinal))
        {
            return;
        }

        throw new UnauthorizedAccessException("No autorizado para operaciones de sync.");
    }

    private string GetChangedBy()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return User.FindFirstValue("display_name")
                ?? User.FindFirstValue(ClaimTypes.Name)
                ?? "sync-admin";
        }

        return "sync-key";
    }
}
