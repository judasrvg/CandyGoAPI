using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [AllowAnonymous]
    [HttpGet("public")]
    public async Task<ActionResult<PublicSettingsDto>> GetPublicSettings()
    {
        var rules = await _settingsService.GetBusinessRulesAsync();
        return Ok(new PublicSettingsDto
        {
            DeliveryFee = rules.DeliveryFee,
            RewardPercent = rules.RewardPercent,
            CashConversionRate = rules.CashConversionRate
        });
    }
}
