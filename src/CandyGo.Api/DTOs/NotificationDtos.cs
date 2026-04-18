namespace CandyGo.Api.DTOs;

public sealed class PushPublicKeyDto
{
    public bool Enabled { get; set; }
    public string PublicKey { get; set; } = string.Empty;
}

public sealed class PushSubscriptionKeysDto
{
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}

public sealed class UpsertPushSubscriptionRequest
{
    public string Endpoint { get; set; } = string.Empty;
    public PushSubscriptionKeysDto Keys { get; set; } = new();
    public string? ContentEncoding { get; set; }
    public string? UserAgent { get; set; }
}

public sealed class RemovePushSubscriptionRequest
{
    public string Endpoint { get; set; } = string.Empty;
}

public sealed class PushSubscriptionStatusDto
{
    public long SubscriptionId { get; set; }
    public bool IsActive { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}

public sealed class NotificationTemplateDto
{
    public long Id { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MessageBody { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string? TargetUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class CreateNotificationTemplateRequest
{
    public string TemplateName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MessageBody { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string? TargetUrl { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class UpdateNotificationTemplateRequest
{
    public string Title { get; set; } = string.Empty;
    public string MessageBody { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string? TargetUrl { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SendNotificationCampaignRequest
{
    public long? TemplateId { get; set; }
    public string? Title { get; set; }
    public string? MessageBody { get; set; }
    public string? IconUrl { get; set; }
    public string? TargetUrl { get; set; }
    public string AudienceType { get; set; } = "ALL";
    public IReadOnlyList<long> ClientIds { get; set; } = Array.Empty<long>();
}

public sealed class NotificationCampaignDto
{
    public long Id { get; set; }
    public long? TemplateId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string MessageBody { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string? TargetUrl { get; set; }
    public string AudienceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalTargets { get; set; }
    public int TotalSent { get; set; }
    public int TotalFailed { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAtUtc { get; set; }
}

public sealed class NotificationCampaignResultDto
{
    public long CampaignId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalTargets { get; set; }
    public int TotalSent { get; set; }
    public int TotalFailed { get; set; }
}
