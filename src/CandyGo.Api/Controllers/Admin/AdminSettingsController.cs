using System.Security.Claims;
using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/settings")]
public sealed class AdminSettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;

    public AdminSettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet("business-rules")]
    public async Task<ActionResult<BusinessRuleDto>> GetBusinessRules()
    {
        var data = await _settingsService.GetBusinessRulesAsync();
        return Ok(data);
    }

    [HttpPut("business-rules")]
    public async Task<ActionResult<BusinessRuleDto>> UpdateBusinessRules([FromBody] UpdateBusinessRuleRequest request)
    {
        var changedBy = User.FindFirstValue("display_name")
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? "admin";

        var updated = await _settingsService.UpdateBusinessRulesAsync(request, changedBy);
        return Ok(updated);
    }
}
