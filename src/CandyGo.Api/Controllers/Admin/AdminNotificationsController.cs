using System.Security.Claims;
using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/notifications")]
public sealed class AdminNotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public AdminNotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet("templates")]
    public async Task<ActionResult<IReadOnlyList<NotificationTemplateDto>>> GetTemplates([FromQuery] bool includeInactive = true)
    {
        var data = await _notificationService.GetTemplatesAsync(includeInactive);
        return Ok(data);
    }

    [HttpPost("templates")]
    public async Task<ActionResult<NotificationTemplateDto>> CreateTemplate([FromBody] CreateNotificationTemplateRequest request)
    {
        var changedBy = GetChangedBy();
        var created = await _notificationService.CreateTemplateAsync(request, changedBy);
        return CreatedAtAction(nameof(GetTemplates), new { includeInactive = true }, created);
    }

    [HttpPut("templates/{templateId:long}")]
    public async Task<ActionResult<NotificationTemplateDto>> UpdateTemplate(long templateId, [FromBody] UpdateNotificationTemplateRequest request)
    {
        var changedBy = GetChangedBy();
        var updated = await _notificationService.UpdateTemplateAsync(templateId, request, changedBy);
        return Ok(updated);
    }

    [HttpGet("campaigns")]
    public async Task<ActionResult<IReadOnlyList<NotificationCampaignDto>>> GetCampaigns([FromQuery] int take = 80)
    {
        var data = await _notificationService.GetCampaignsAsync(take);
        return Ok(data);
    }

    [HttpPost("campaigns/send")]
    public async Task<ActionResult<NotificationCampaignResultDto>> SendCampaign(
        [FromBody] SendNotificationCampaignRequest request,
        CancellationToken cancellationToken)
    {
        var changedBy = GetChangedBy();
        var result = await _notificationService.SendCampaignAsync(request, changedBy, cancellationToken);
        return Ok(result);
    }

    private string GetChangedBy()
    {
        return User.FindFirstValue("display_name")
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? "admin";
    }
}
