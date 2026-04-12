using System.Security.Claims;
using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/wallet")]
public sealed class AdminWalletController : ControllerBase
{
    private readonly IWalletService _walletService;

    public AdminWalletController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpGet("client/{clientId:long}")]
    public async Task<ActionResult<WalletSummaryDto>> GetByClientId(long clientId, [FromQuery] int movementLimit = 50)
    {
        var wallet = await _walletService.GetByClientIdAsync(clientId, movementLimit);
        return Ok(wallet);
    }

    [HttpPost("client/{clientId:long}/adjust")]
    public async Task<ActionResult<WalletAdjustmentResultDto>> AdjustWallet(long clientId, [FromBody] AdminAdjustWalletRequest request)
    {
        var changedBy = User.FindFirstValue("display_name")
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? $"admin:{User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown"}";

        var result = await _walletService.AdjustByAdminAsync(clientId, request, changedBy);
        return Ok(result);
    }
}
