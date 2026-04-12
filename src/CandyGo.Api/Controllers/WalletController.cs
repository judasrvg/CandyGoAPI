using System.Security.Claims;
using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers;

[ApiController]
[Authorize(Roles = "client")]
[Route("api/wallet")]
public sealed class WalletController : ControllerBase
{
    private readonly IWalletService _walletService;

    public WalletController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpGet("mine")]
    public async Task<ActionResult<WalletSummaryDto>> GetMyWallet([FromQuery] int movementLimit = 30)
    {
        var clientId = GetCurrentUserId();
        var wallet = await _walletService.GetByClientIdAsync(clientId, movementLimit);
        return Ok(wallet);
    }

    private long GetCurrentUserId()
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
