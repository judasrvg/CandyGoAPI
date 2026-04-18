using System.Security.Claims;
using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/contests")]
public sealed class AdminContestsController : ControllerBase
{
    private readonly IContestService _contestService;

    public AdminContestsController(IContestService contestService)
    {
        _contestService = contestService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminContestDto>>> GetAll([FromQuery] bool includeInactive = true)
    {
        var data = await _contestService.GetAdminContestsAsync(includeInactive);
        return Ok(data);
    }

    [HttpPost]
    public async Task<ActionResult<AdminContestDto>> Create([FromBody] CreateContestRequest request)
    {
        var changedBy = GetChangedBy();
        var created = await _contestService.CreateContestAsync(request, changedBy);
        return CreatedAtAction(nameof(GetAll), new { includeInactive = true }, created);
    }

    [HttpPut("{contestId:long}")]
    public async Task<ActionResult<AdminContestDto>> Update(long contestId, [FromBody] UpdateContestRequest request)
    {
        var changedBy = GetChangedBy();
        var updated = await _contestService.UpdateContestAsync(contestId, request, changedBy);
        return Ok(updated);
    }

    [HttpPut("{contestId:long}/targets")]
    public async Task<ActionResult<AdminContestDto>> UpdateTargets(long contestId, [FromBody] UpdateContestTargetsRequest request)
    {
        var changedBy = GetChangedBy();
        var updated = await _contestService.UpdateTargetsAsync(contestId, request, changedBy);
        return Ok(updated);
    }

    [HttpGet("{contestId:long}/plays")]
    public async Task<ActionResult<IReadOnlyList<ContestPlayTraceDto>>> GetPlays(long contestId, [FromQuery] int take = 150)
    {
        var data = await _contestService.GetContestPlaysAsync(contestId, take);
        return Ok(data);
    }

    private string GetChangedBy()
    {
        return User.FindFirstValue("display_name")
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? "admin";
    }
}
