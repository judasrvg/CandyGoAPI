using System.Net;
using System.Text.Json;
using CandyGo.Api.DTOs;
using CandyGo.Api.Infrastructure;
using CandyGo.Api.Security;
using Dapper;
using Microsoft.Extensions.Options;
using WebPush;

namespace CandyGo.Api.Services;

public interface INotificationService
{
    Task<PushPublicKeyDto> GetPublicKeyAsync();
    Task<PushSubscriptionStatusDto> UpsertClientSubscriptionAsync(long clientId, UpsertPushSubscriptionRequest request);
    Task RemoveClientSubscriptionAsync(long clientId, RemovePushSubscriptionRequest request);
    Task<IReadOnlyList<NotificationTemplateDto>> GetTemplatesAsync(bool includeInactive = true);
    Task<NotificationTemplateDto> CreateTemplateAsync(CreateNotificationTemplateRequest request, string changedBy);
    Task<NotificationTemplateDto> UpdateTemplateAsync(long templateId, UpdateNotificationTemplateRequest request, string changedBy);
    Task<IReadOnlyList<NotificationCampaignDto>> GetCampaignsAsync(int take = 80);
    Task<NotificationCampaignResultDto> SendCampaignAsync(SendNotificationCampaignRequest request, string changedBy, CancellationToken cancellationToken);
}

public sealed class NotificationService : INotificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly PushOptions _pushOptions;
    private readonly WebPushClient _webPushClient;

    public NotificationService(
        IDbConnectionFactory connectionFactory,
        IOptions<PushOptions> pushOptions)
    {
        _connectionFactory = connectionFactory;
        _pushOptions = pushOptions.Value;
        _webPushClient = new WebPushClient();
    }

    public Task<PushPublicKeyDto> GetPublicKeyAsync()
    {
        var enabled = IsPushConfigured();
        return Task.FromResult(new PushPublicKeyDto
        {
            Enabled = enabled,
            PublicKey = enabled ? _pushOptions.VapidPublicKey.Trim() : string.Empty
        });
    }

    public async Task<PushSubscriptionStatusDto> UpsertClientSubscriptionAsync(long clientId, UpsertPushSubscriptionRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clientId);

        var endpoint = NormalizeRequired(request.Endpoint, "endpoint", 700, 20);
        var p256dh = NormalizeRequired(request.Keys?.P256dh, "keys.p256dh", 255, 20);
        var auth = NormalizeRequired(request.Keys?.Auth, "keys.auth", 120, 8);
        var encoding = NormalizeOptional(request.ContentEncoding, 40);
        var userAgent = NormalizeOptional(request.UserAgent, 320);

        await using var connection = _connectionFactory.CreateConnection();

        var existing = await connection.QuerySingleOrDefaultAsync<PushSubscriptionRow>(@"
SELECT TOP (1)
    id,
    client_id,
    endpoint,
    is_active,
    last_seen_at
FROM cg.push_subscriptions
WHERE endpoint = @Endpoint",
            new { Endpoint = endpoint });

        if (existing is null)
        {
            var newId = await connection.ExecuteScalarAsync<long>(@"
INSERT INTO cg.push_subscriptions
(
    client_id,
    endpoint,
    p256dh,
    auth_secret,
    content_encoding,
    user_agent,
    is_active,
    updated_at,
    last_seen_at
)
VALUES
(
    @ClientId,
    @Endpoint,
    @P256dh,
    @Auth,
    @ContentEncoding,
    @UserAgent,
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                new
                {
                    ClientId = clientId,
                    Endpoint = endpoint,
                    P256dh = p256dh,
                    Auth = auth,
                    ContentEncoding = encoding,
                    UserAgent = userAgent
                });

            return new PushSubscriptionStatusDto
            {
                SubscriptionId = newId,
                IsActive = true,
                LastSeenAtUtc = DateTime.UtcNow
            };
        }

        await connection.ExecuteAsync(@"
UPDATE cg.push_subscriptions
SET client_id = @ClientId,
    p256dh = @P256dh,
    auth_secret = @Auth,
    content_encoding = @ContentEncoding,
    user_agent = @UserAgent,
    is_active = 1,
    updated_at = SYSUTCDATETIME(),
    last_seen_at = SYSUTCDATETIME()
WHERE id = @Id",
            new
            {
                ClientId = clientId,
                P256dh = p256dh,
                Auth = auth,
                ContentEncoding = encoding,
                UserAgent = userAgent,
                existing.Id
            });

        return new PushSubscriptionStatusDto
        {
            SubscriptionId = existing.Id,
            IsActive = true,
            LastSeenAtUtc = DateTime.UtcNow
        };
    }

    public async Task RemoveClientSubscriptionAsync(long clientId, RemovePushSubscriptionRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clientId);

        var endpoint = NormalizeRequired(request.Endpoint, "endpoint", 700, 20);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
UPDATE cg.push_subscriptions
SET is_active = 0,
    updated_at = SYSUTCDATETIME(),
    last_seen_at = SYSUTCDATETIME()
WHERE client_id = @ClientId
  AND endpoint = @Endpoint",
            new
            {
                ClientId = clientId,
                Endpoint = endpoint
            });
    }

    public async Task<IReadOnlyList<NotificationTemplateDto>> GetTemplatesAsync(bool includeInactive = true)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<NotificationTemplateDto>(@"
SELECT
    id,
    template_name,
    title,
    message_body,
    icon_url,
    target_url,
    is_active,
    updated_at,
    updated_by
FROM cg.notification_templates
WHERE @IncludeInactive = 1 OR is_active = 1
ORDER BY is_active DESC, updated_at DESC, id DESC",
            new { IncludeInactive = includeInactive ? 1 : 0 });

        return rows.ToList();
    }

    public async Task<NotificationTemplateDto> CreateTemplateAsync(CreateNotificationTemplateRequest request, string changedBy)
    {
        var name = NormalizeRequired(request.TemplateName, "templateName", 90, 3).ToUpperInvariant();
        var title = NormalizeRequired(request.Title, "title", 120, 3);
        var message = NormalizeRequired(request.MessageBody, "messageBody", 450, 3);
        var icon = NormalizeOptional(request.IconUrl, 450);
        var target = NormalizeOptional(request.TargetUrl, 320);

        await using var connection = _connectionFactory.CreateConnection();

        var id = await connection.ExecuteScalarAsync<long>(@"
INSERT INTO cg.notification_templates
(
    template_name,
    title,
    message_body,
    icon_url,
    target_url,
    is_active,
    created_by,
    updated_by,
    created_at,
    updated_at
)
VALUES
(
    @TemplateName,
    @Title,
    @MessageBody,
    @IconUrl,
    @TargetUrl,
    @IsActive,
    @ChangedBy,
    @ChangedBy,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new
            {
                TemplateName = name,
                Title = title,
                MessageBody = message,
                IconUrl = icon,
                TargetUrl = target,
                IsActive = request.IsActive,
                ChangedBy = NormalizeOptional(changedBy, 120) ?? "admin"
            });

        return await GetTemplateByIdAsync(connection, id);
    }

    public async Task<NotificationTemplateDto> UpdateTemplateAsync(long templateId, UpdateNotificationTemplateRequest request, string changedBy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(templateId);

        var title = NormalizeRequired(request.Title, "title", 120, 3);
        var message = NormalizeRequired(request.MessageBody, "messageBody", 450, 3);
        var icon = NormalizeOptional(request.IconUrl, 450);
        var target = NormalizeOptional(request.TargetUrl, 320);

        await using var connection = _connectionFactory.CreateConnection();

        var affected = await connection.ExecuteAsync(@"
UPDATE cg.notification_templates
SET title = @Title,
    message_body = @MessageBody,
    icon_url = @IconUrl,
    target_url = @TargetUrl,
    is_active = @IsActive,
    updated_by = @ChangedBy,
    updated_at = SYSUTCDATETIME()
WHERE id = @TemplateId",
            new
            {
                TemplateId = templateId,
                Title = title,
                MessageBody = message,
                IconUrl = icon,
                TargetUrl = target,
                IsActive = request.IsActive,
                ChangedBy = NormalizeOptional(changedBy, 120) ?? "admin"
            });

        if (affected == 0)
        {
            throw new KeyNotFoundException("Plantilla de notificación no encontrada.");
        }

        return await GetTemplateByIdAsync(connection, templateId);
    }

    public async Task<IReadOnlyList<NotificationCampaignDto>> GetCampaignsAsync(int take = 80)
    {
        if (take <= 0 || take > 300)
        {
            take = 80;
        }

        await using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<NotificationCampaignDto>(@"
SELECT TOP (@Take)
    id,
    template_id,
    title,
    message_body,
    icon_url,
    target_url,
    audience_type,
    status,
    total_targets,
    total_sent,
    total_failed,
    created_by,
    created_at,
    sent_at_utc
FROM cg.notification_campaigns
ORDER BY created_at DESC, id DESC",
            new { Take = take });

        return rows.ToList();
    }

    public async Task<NotificationCampaignResultDto> SendCampaignAsync(
        SendNotificationCampaignRequest request,
        string changedBy,
        CancellationToken cancellationToken)
    {
        EnsurePushConfigured();

        var normalizedAudience = NormalizeAudienceType(request.AudienceType);
        var normalizedChangedBy = NormalizeOptional(changedBy, 120) ?? "admin";

        await using var connection = _connectionFactory.CreateConnection();

        var template = request.TemplateId.HasValue
            ? await GetTemplateByIdAsync(connection, request.TemplateId.Value)
            : null;

        var title = NormalizeOptional(request.Title, 120) ?? template?.Title;
        var message = NormalizeOptional(request.MessageBody, 450) ?? template?.MessageBody;

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Debes indicar título y mensaje, o seleccionar una plantilla válida.");
        }

        var icon = NormalizeOptional(request.IconUrl, 450)
            ?? template?.IconUrl
            ?? NormalizeOptional(_pushOptions.DefaultIconUrl, 450)
            ?? "/assets/candygo-icon.svg";

        var target = NormalizeOptional(request.TargetUrl, 320)
            ?? template?.TargetUrl
            ?? NormalizeOptional(_pushOptions.DefaultTargetUrl, 320)
            ?? "/";

        var candidateClientIds = (request.ClientIds ?? Array.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (normalizedAudience == "CLIENTS" && candidateClientIds.Length == 0)
        {
            throw new ArgumentException("Debes seleccionar al menos un cliente para este envío.");
        }

        var subscriptions = (await connection.QueryAsync<PushSendRow>(normalizedAudience == "ALL"
            ? @"
SELECT
    s.id,
    s.client_id,
    s.endpoint,
    s.p256dh,
    s.auth_secret,
    s.content_encoding
FROM cg.push_subscriptions s
INNER JOIN cg.clients c ON c.id = s.client_id
WHERE s.is_active = 1
  AND c.is_active = 1"
            : @"
SELECT
    s.id,
    s.client_id,
    s.endpoint,
    s.p256dh,
    s.auth_secret,
    s.content_encoding
FROM cg.push_subscriptions s
INNER JOIN cg.clients c ON c.id = s.client_id
WHERE s.is_active = 1
  AND c.is_active = 1
  AND s.client_id IN @ClientIds",
            new { ClientIds = candidateClientIds })).ToList();

        var targetedClientIds = subscriptions
            .Select(x => x.ClientId)
            .Distinct()
            .ToArray();

        var audiencePayload = normalizedAudience == "CLIENTS"
            ? JsonSerializer.Serialize(new { clientIds = candidateClientIds }, JsonOptions)
            : null;

        var campaignId = await connection.ExecuteScalarAsync<long>(@"
INSERT INTO cg.notification_campaigns
(
    template_id,
    title,
    message_body,
    icon_url,
    target_url,
    audience_type,
    status,
    audience_payload_json,
    total_targets,
    total_sent,
    total_failed,
    created_by,
    created_at,
    sent_at_utc
)
VALUES
(
    @TemplateId,
    @Title,
    @MessageBody,
    @IconUrl,
    @TargetUrl,
    @AudienceType,
    @Status,
    @AudiencePayload,
    @TotalTargets,
    0,
    0,
    @CreatedBy,
    SYSUTCDATETIME(),
    CASE WHEN @TotalTargets = 0 THEN SYSUTCDATETIME() ELSE NULL END
);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new
            {
                TemplateId = request.TemplateId,
                Title = title,
                MessageBody = message,
                IconUrl = icon,
                TargetUrl = target,
                AudienceType = normalizedAudience,
                Status = subscriptions.Count == 0 ? "FAILED" : "QUEUED",
                AudiencePayload = audiencePayload,
                TotalTargets = targetedClientIds.Length,
                CreatedBy = normalizedChangedBy
            });

        if (targetedClientIds.Length > 0)
        {
            await connection.ExecuteAsync(@"
INSERT INTO cg.notification_campaign_targets (campaign_id, client_id)
SELECT @CampaignId, v.client_id
FROM (SELECT DISTINCT value AS client_id FROM STRING_SPLIT(@ClientIdListCsv, ',')) v
WHERE TRY_CAST(v.client_id AS BIGINT) IS NOT NULL;",
                new
                {
                    CampaignId = campaignId,
                    ClientIdListCsv = string.Join(',', targetedClientIds)
                });
        }

        if (subscriptions.Count == 0)
        {
            return new NotificationCampaignResultDto
            {
                CampaignId = campaignId,
                Status = "FAILED",
                TotalTargets = 0,
                TotalSent = 0,
                TotalFailed = 0
            };
        }

        var payload = JsonSerializer.Serialize(new
        {
            title,
            body = message,
            icon,
            url = target,
            campaignId
        }, JsonOptions);

        var vapidDetails = new VapidDetails(
            _pushOptions.VapidSubject.Trim(),
            _pushOptions.VapidPublicKey.Trim(),
            _pushOptions.VapidPrivateKey.Trim());

        var sent = 0;
        var failed = 0;

        foreach (var subscriptionRow in subscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var deliveryStatus = "FAILED";
            int? httpStatusCode = null;
            string? errorMessage = null;

            try
            {
                var pushSubscription = new PushSubscription(
                    subscriptionRow.Endpoint,
                    subscriptionRow.P256dh,
                    subscriptionRow.AuthSecret);

                await _webPushClient.SendNotificationAsync(pushSubscription, payload, vapidDetails, cancellationToken);

                sent += 1;
                deliveryStatus = "SENT";
            }
            catch (WebPushException ex)
            {
                failed += 1;
                httpStatusCode = (int)ex.StatusCode;
                errorMessage = NormalizeOptional(ex.Message, 500);

                if (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                {
                    deliveryStatus = "EXPIRED";
                    await connection.ExecuteAsync(@"
UPDATE cg.push_subscriptions
SET is_active = 0,
    updated_at = SYSUTCDATETIME(),
    last_seen_at = SYSUTCDATETIME()
WHERE id = @Id",
                        new { subscriptionRow.Id });
                }
            }
            catch (Exception ex)
            {
                failed += 1;
                errorMessage = NormalizeOptional(ex.Message, 500);
            }

            await connection.ExecuteAsync(@"
INSERT INTO cg.notification_deliveries
(
    campaign_id,
    client_id,
    subscription_id,
    status,
    http_status_code,
    error_message,
    created_at,
    delivered_at_utc
)
VALUES
(
    @CampaignId,
    @ClientId,
    @SubscriptionId,
    @Status,
    @HttpStatusCode,
    @ErrorMessage,
    SYSUTCDATETIME(),
    CASE WHEN @Status = 'SENT' THEN SYSUTCDATETIME() ELSE NULL END
)",
                new
                {
                    CampaignId = campaignId,
                    subscriptionRow.ClientId,
                    SubscriptionId = subscriptionRow.Id,
                    Status = deliveryStatus,
                    HttpStatusCode = httpStatusCode,
                    ErrorMessage = errorMessage
                });
        }

        var finalStatus = failed == 0
            ? "SENT"
            : sent == 0
                ? "FAILED"
                : "PARTIAL";

        await connection.ExecuteAsync(@"
UPDATE cg.notification_campaigns
SET status = @Status,
    total_sent = @TotalSent,
    total_failed = @TotalFailed,
    sent_at_utc = SYSUTCDATETIME()
WHERE id = @CampaignId",
            new
            {
                Status = finalStatus,
                TotalSent = sent,
                TotalFailed = failed,
                CampaignId = campaignId
            });

        return new NotificationCampaignResultDto
        {
            CampaignId = campaignId,
            Status = finalStatus,
            TotalTargets = targetedClientIds.Length,
            TotalSent = sent,
            TotalFailed = failed
        };
    }

    private async Task<NotificationTemplateDto> GetTemplateByIdAsync(System.Data.IDbConnection connection, long templateId)
    {
        var template = await connection.QuerySingleOrDefaultAsync<NotificationTemplateDto>(@"
SELECT
    id,
    template_name,
    title,
    message_body,
    icon_url,
    target_url,
    is_active,
    updated_at,
    updated_by
FROM cg.notification_templates
WHERE id = @Id",
            new { Id = templateId });

        if (template is null)
        {
            throw new KeyNotFoundException("Plantilla de notificación no encontrada.");
        }

        return template;
    }

    private void EnsurePushConfigured()
    {
        if (IsPushConfigured())
        {
            return;
        }

        throw new InvalidOperationException("Push web no configurado. Define Push:VapidSubject, Push:VapidPublicKey y Push:VapidPrivateKey.");
    }

    private bool IsPushConfigured()
    {
        return !string.IsNullOrWhiteSpace(_pushOptions.VapidSubject)
            && !string.IsNullOrWhiteSpace(_pushOptions.VapidPublicKey)
            && !string.IsNullOrWhiteSpace(_pushOptions.VapidPrivateKey);
    }

    private static string NormalizeAudienceType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "ALL" => "ALL",
            "CLIENTS" => "CLIENTS",
            _ => throw new ArgumentException("AudienceType inválido. Valores permitidos: ALL | CLIENTS.")
        };
    }

    private static string NormalizeRequired(string? value, string fieldName, int maxLength, int minLength = 1)
    {
        var text = NormalizeOptional(value, maxLength);
        if (string.IsNullOrWhiteSpace(text) || text.Length < minLength)
        {
            throw new ArgumentException($"{fieldName} es requerido.");
        }

        return text;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Length <= maxLength
            ? text
            : text[..maxLength];
    }

    private sealed class PushSubscriptionRow
    {
        public long Id { get; set; }
        public long ClientId { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime LastSeenAt { get; set; }
    }

    private sealed class PushSendRow
    {
        public long Id { get; set; }
        public long ClientId { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string AuthSecret { get; set; } = string.Empty;
        public string? ContentEncoding { get; set; }
    }
}
