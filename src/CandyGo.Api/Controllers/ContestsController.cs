using System.Security.Claims;
using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers;

[ApiController]
[Authorize(Roles = "client")]
[Route("api/contests")]
public sealed class ContestsController : ControllerBase
{
    private readonly IContestService _contestService;

    public ContestsController(IContestService contestService)
    {
        _contestService = contestService;
    }

    [HttpGet("active")]
    public async Task<ActionResult<IReadOnlyList<ClientContestDto>>> GetActive()
    {
        var clientId = GetCurrentClientId();
        var data = await _contestService.GetActiveContestsForClientAsync(clientId);
        return Ok(data);
    }

    [HttpPost("{contestId:long}/play")]
    public async Task<ActionResult<ContestPlayResultDto>> Play(
        long contestId,
        [FromBody] PlayContestRequest request)
    {
        var clientId = GetCurrentClientId();
        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await _contestService.PlayContestAsync(clientId, contestId, request, sourceIp, userAgent);
        return Ok(result);
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
