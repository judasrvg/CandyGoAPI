using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CandyGo.Api.DTOs;
using CandyGo.Api.Infrastructure;
using CandyGo.Api.Security;
using Dapper;

namespace CandyGo.Api.Services;

public interface IContestService
{
    Task<IReadOnlyList<AdminContestDto>> GetAdminContestsAsync(bool includeInactive = true);
    Task<AdminContestDto> CreateContestAsync(CreateContestRequest request, string changedBy);
    Task<AdminContestDto> UpdateContestAsync(long contestId, UpdateContestRequest request, string changedBy);
    Task<AdminContestDto> UpdateTargetsAsync(long contestId, UpdateContestTargetsRequest request, string changedBy);
    Task<IReadOnlyList<ContestPlayTraceDto>> GetContestPlaysAsync(long contestId, int take = 150);
    Task<IReadOnlyList<ClientContestDto>> GetActiveContestsForClientAsync(long clientId);
    Task<ContestPlayResultDto> PlayContestAsync(long clientId, long contestId, PlayContestRequest request, string? sourceIp, string? userAgent);
}

public sealed class ContestService : IContestService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IDbConnectionFactory _connectionFactory;

    public ContestService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AdminContestDto>> GetAdminContestsAsync(bool includeInactive = true)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var contests = (await connection.QueryAsync<ContestAdminRow>(@"
SELECT
    c.id,
    c.contest_name,
    c.contest_slug,
    c.contest_type,
    c.description,
    c.icon_name,
    c.config_json,
    c.audience_type,
    c.max_plays_per_client,
    c.starts_at_utc,
    c.ends_at_utc,
    c.is_active,
    c.updated_at,
    c.updated_by,
    ISNULL(stats.total_plays, 0) AS total_plays,
    ISNULL(stats.total_winners, 0) AS total_winners
FROM cg.contests c
OUTER APPLY
(
    SELECT
        COUNT(1) AS total_plays,
        SUM(CASE WHEN cp.awarded_candycash > 0 THEN 1 ELSE 0 END) AS total_winners
    FROM cg.contest_plays cp
    WHERE cp.contest_id = c.id
) stats
WHERE @IncludeInactive = 1 OR c.is_active = 1
ORDER BY c.updated_at DESC, c.id DESC",
            new { IncludeInactive = includeInactive ? 1 : 0 })).ToList();

        if (contests.Count == 0)
        {
            return Array.Empty<AdminContestDto>();
        }

        var contestIds = contests.Select(x => x.Id).Distinct().ToArray();
        var targets = await connection.QueryAsync<ContestTargetRow>(@"
SELECT contest_id,
       client_id
FROM cg.contest_targets
WHERE contest_id IN @ContestIds
  AND is_enabled = 1",
            new { ContestIds = contestIds });

        var targetsByContest = targets
            .GroupBy(x => x.ContestId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<long>)g.Select(x => x.ClientId).Distinct().ToList());

        return contests.Select(row =>
        {
            targetsByContest.TryGetValue(row.Id, out var targetClientIds);
            return MapAdminContest(row, targetClientIds ?? Array.Empty<long>());
        }).ToList();
    }

    public async Task<AdminContestDto> CreateContestAsync(CreateContestRequest request, string changedBy)
    {
        var normalized = NormalizeContestInput(
            request.ContestName,
            request.ContestSlug,
            request.Description,
            request.IconName,
            request.AudienceType,
            request.MaxPlaysPerClient,
            request.StartsAtUtc,
            request.EndsAtUtc,
            request.IsActive,
            request.Config,
            request.TargetClientIds);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            var contestId = await connection.ExecuteScalarAsync<long>(@"
INSERT INTO cg.contests
(
    contest_name,
    contest_slug,
    contest_type,
    description,
    icon_name,
    config_json,
    audience_type,
    max_plays_per_client,
    starts_at_utc,
    ends_at_utc,
    is_active,
    created_by,
    updated_by,
    created_at,
    updated_at
)
VALUES
(
    @ContestName,
    @ContestSlug,
    'PICK_A_BOX',
    @Description,
    @IconName,
    @ConfigJson,
    @AudienceType,
    @MaxPlaysPerClient,
    @StartsAtUtc,
    @EndsAtUtc,
    @IsActive,
    @ChangedBy,
    @ChangedBy,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                new
                {
                    normalized.ContestName,
                    normalized.ContestSlug,
                    normalized.Description,
                    normalized.IconName,
                    normalized.ConfigJson,
                    normalized.AudienceType,
                    normalized.MaxPlaysPerClient,
                    normalized.StartsAtUtc,
                    normalized.EndsAtUtc,
                    normalized.IsActive,
                    ChangedBy = NormalizeOptional(changedBy, 120) ?? "admin"
                },
                tx);

            await ReplaceTargetsAsync(connection, tx, contestId, normalized.AudienceType, normalized.TargetClientIds);
            await tx.CommitAsync();

            return await GetAdminContestByIdAsync(contestId);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<AdminContestDto> UpdateContestAsync(long contestId, UpdateContestRequest request, string changedBy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contestId);

        var normalized = NormalizeContestInput(
            request.ContestName,
            request.ContestSlug,
            request.Description,
            request.IconName,
            request.AudienceType,
            request.MaxPlaysPerClient,
            request.StartsAtUtc,
            request.EndsAtUtc,
            request.IsActive,
            request.Config,
            request.TargetClientIds);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            var affected = await connection.ExecuteAsync(@"
UPDATE cg.contests
SET contest_name = @ContestName,
    contest_slug = @ContestSlug,
    description = @Description,
    icon_name = @IconName,
    config_json = @ConfigJson,
    audience_type = @AudienceType,
    max_plays_per_client = @MaxPlaysPerClient,
    starts_at_utc = @StartsAtUtc,
    ends_at_utc = @EndsAtUtc,
    is_active = @IsActive,
    updated_by = @ChangedBy,
    updated_at = SYSUTCDATETIME()
WHERE id = @ContestId",
                new
                {
                    ContestId = contestId,
                    normalized.ContestName,
                    normalized.ContestSlug,
                    normalized.Description,
                    normalized.IconName,
                    normalized.ConfigJson,
                    normalized.AudienceType,
                    normalized.MaxPlaysPerClient,
                    normalized.StartsAtUtc,
                    normalized.EndsAtUtc,
                    normalized.IsActive,
                    ChangedBy = NormalizeOptional(changedBy, 120) ?? "admin"
                },
                tx);

            if (affected == 0)
            {
                throw new KeyNotFoundException("Concurso no encontrado.");
            }

            await ReplaceTargetsAsync(connection, tx, contestId, normalized.AudienceType, normalized.TargetClientIds);
            await tx.CommitAsync();

            return await GetAdminContestByIdAsync(contestId);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<AdminContestDto> UpdateTargetsAsync(long contestId, UpdateContestTargetsRequest request, string changedBy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contestId);

        var targetClientIds = NormalizeClientIds(request.ClientIds);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            var audienceType = await connection.QuerySingleOrDefaultAsync<string>(@"
SELECT audience_type
FROM cg.contests
WHERE id = @Id",
                new { Id = contestId }, tx);

            if (audienceType is null)
            {
                throw new KeyNotFoundException("Concurso no encontrado.");
            }

            var normalizedAudience = NormalizeAudienceType(audienceType);
            await ReplaceTargetsAsync(connection, tx, contestId, normalizedAudience, targetClientIds);

            await connection.ExecuteAsync(@"
UPDATE cg.contests
SET updated_by = @ChangedBy,
    updated_at = SYSUTCDATETIME()
WHERE id = @ContestId",
                new
                {
                    ContestId = contestId,
                    ChangedBy = NormalizeOptional(changedBy, 120) ?? "admin"
                },
                tx);

            await tx.CommitAsync();
            return await GetAdminContestByIdAsync(contestId);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<ContestPlayTraceDto>> GetContestPlaysAsync(long contestId, int take = 150)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contestId);

        if (take <= 0 || take > 500)
        {
            take = 150;
        }

        await using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ContestPlayTraceDto>(@"
SELECT TOP (@Take)
    cp.id,
    cp.client_id,
    c.full_name AS client_name,
    c.phone AS client_phone,
    cp.selected_slot,
    cp.result_code,
    cp.result_message,
    cp.awarded_candycash,
    cp.wallet_movement_id,
    cp.played_at
FROM cg.contest_plays cp
INNER JOIN cg.clients c ON c.id = cp.client_id
WHERE cp.contest_id = @ContestId
ORDER BY cp.played_at DESC, cp.id DESC",
            new
            {
                Take = take,
                ContestId = contestId
            });

        return rows.ToList();
    }

    public async Task<IReadOnlyList<ClientContestDto>> GetActiveContestsForClientAsync(long clientId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clientId);

        await using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<ClientContestRow>(@"
SELECT
    c.id,
    c.contest_name,
    c.description,
    c.icon_name,
    c.config_json,
    c.max_plays_per_client,
    c.ends_at_utc,
    ISNULL(plays.used_plays, 0) AS used_plays
FROM cg.contests c
OUTER APPLY
(
    SELECT COUNT(1) AS used_plays
    FROM cg.contest_plays cp
    WHERE cp.contest_id = c.id
      AND cp.client_id = @ClientId
) plays
WHERE c.is_active = 1
  AND (c.starts_at_utc IS NULL OR c.starts_at_utc <= SYSUTCDATETIME())
  AND (c.ends_at_utc IS NULL OR c.ends_at_utc >= SYSUTCDATETIME())
  AND (
      c.audience_type = 'ALL'
      OR EXISTS (
          SELECT 1
          FROM cg.contest_targets t
          WHERE t.contest_id = c.id
            AND t.client_id = @ClientId
            AND t.is_enabled = 1
      )
  )
ORDER BY c.updated_at DESC, c.id DESC",
            new { ClientId = clientId });

        var activeContests = new List<ClientContestDto>();
        foreach (var row in rows)
        {
            var config = ParseContestConfig(row.ConfigJson);
            var remaining = Math.Max(0, row.MaxPlaysPerClient - row.UsedPlays);
            if (remaining <= 0)
            {
                continue;
            }

            activeContests.Add(new ClientContestDto
            {
                Id = row.Id,
                ContestName = row.ContestName,
                Description = row.Description,
                IconName = row.IconName,
                MaxPlaysPerClient = row.MaxPlaysPerClient,
                UsedPlays = row.UsedPlays,
                RemainingPlays = remaining,
                EndsAtUtc = row.EndsAtUtc,
                Boxes = config.Boxes
                    .OrderBy(x => x.Slot)
                    .Select(x => new ClientContestBoxDto
                    {
                        Slot = x.Slot,
                        Label = x.Label
                    })
                    .ToList()
            });
        }

        return activeContests;
    }

    public async Task<ContestPlayResultDto> PlayContestAsync(long clientId, long contestId, PlayContestRequest request, string? sourceIp, string? userAgent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clientId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contestId);

        var selectedSlot = request.SelectedSlot;
        if (selectedSlot <= 0 || selectedSlot > 12)
        {
            throw new ArgumentException("Selecciona una caja válida.");
        }

        var requestId = request.ClientRequestId ?? Guid.NewGuid();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var contest = await connection.QuerySingleOrDefaultAsync<ContestPlayRow>(@"
SELECT
    id,
    contest_name,
    contest_type,
    config_json,
    audience_type,
    max_plays_per_client,
    starts_at_utc,
    ends_at_utc,
    is_active
FROM cg.contests WITH (UPDLOCK, ROWLOCK)
WHERE id = @Id",
                new { Id = contestId },
                tx);

            if (contest is null)
            {
                throw new KeyNotFoundException("Concurso no encontrado.");
            }

            ValidateContestAvailability(contest);

            if (NormalizeAudienceType(contest.AudienceType) == "CLIENTS")
            {
                var hasTarget = await connection.ExecuteScalarAsync<int>(@"
SELECT CASE WHEN EXISTS(
    SELECT 1
    FROM cg.contest_targets
    WHERE contest_id = @ContestId
      AND client_id = @ClientId
      AND is_enabled = 1
) THEN 1 ELSE 0 END",
                    new
                    {
                        ContestId = contestId,
                        ClientId = clientId
                    },
                    tx);

                if (hasTarget != 1)
                {
                    throw new UnauthorizedAccessException("Este concurso no está habilitado para tu cuenta.");
                }
            }

            var existing = await connection.QuerySingleOrDefaultAsync<ContestExistingPlayRow>(@"
SELECT TOP (1)
    id,
    selected_slot,
    result_code,
    result_message,
    awarded_candycash,
    wallet_movement_id,
    played_at
FROM cg.contest_plays WITH (UPDLOCK, HOLDLOCK)
WHERE contest_id = @ContestId
  AND client_id = @ClientId
  AND client_request_id = @ClientRequestId",
                new
                {
                    ContestId = contestId,
                    ClientId = clientId,
                    ClientRequestId = requestId
                },
                tx);

            var usedPlays = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM cg.contest_plays WITH (UPDLOCK, HOLDLOCK)
WHERE contest_id = @ContestId
  AND client_id = @ClientId",
                new
                {
                    ContestId = contestId,
                    ClientId = clientId
                },
                tx);

            if (existing is not null)
            {
                var walletBalanceExisting = await GetWalletBalanceAsync(connection, tx, clientId);
                var remainingExisting = Math.Max(0, contest.MaxPlaysPerClient - usedPlays);
                await tx.CommitAsync();

                return new ContestPlayResultDto
                {
                    ContestId = contestId,
                    SelectedSlot = existing.SelectedSlot,
                    IsWinner = existing.AwardedCandyCash > 0,
                    AwardedCandyCash = existing.AwardedCandyCash,
                    ResultCode = existing.ResultCode,
                    ResultMessage = existing.ResultMessage ?? BuildResultMessage(ParseContestConfig(contest.ConfigJson), existing.AwardedCandyCash),
                    WalletMovementId = existing.WalletMovementId,
                    WalletBalance = walletBalanceExisting,
                    RemainingPlays = remainingExisting,
                    PlayedAtUtc = existing.PlayedAt
                };
            }

            if (usedPlays >= contest.MaxPlaysPerClient)
            {
                throw new InvalidOperationException("Ya alcanzaste el límite de intentos para este concurso.");
            }

            var config = ParseContestConfig(contest.ConfigJson);
            var orderedBoxes = config.Boxes
                .OrderBy(x => x.Slot)
                .ToList();

            if (!orderedBoxes.Any(x => x.Slot == selectedSlot))
            {
                throw new ArgumentException("La caja seleccionada no está disponible en este concurso.");
            }

            var rewardPool = orderedBoxes
                .Select(x => new ContestRewardCandidate(x.Label, x.RewardCandyCash))
                .ToList();

            ShuffleRewards(rewardPool);

            var selectedIndex = orderedBoxes.FindIndex(x => x.Slot == selectedSlot);
            var selectedReward = rewardPool[selectedIndex];
            var awarded = selectedReward.RewardCandyCash;

            var resultCode = awarded <= 0m
                ? "EMPTY"
                : awarded >= rewardPool.Max(x => x.RewardCandyCash)
                    ? "WIN_TOP"
                    : "WIN";

            var resultMessage = BuildResultMessage(config, awarded);

            long? walletMovementId = null;
            if (awarded > 0m)
            {
                var movementParams = new DynamicParameters();
                movementParams.Add("@ClientId", clientId);
                movementParams.Add("@SignedAmount", awarded);
                movementParams.Add("@Reason", $"Premio concurso {contest.ContestName}");
                movementParams.Add("@SourceType", "CONTEST_REWARD");
                movementParams.Add("@SourceRef", $"{contestId}:{requestId}");
                movementParams.Add("@IdempotencyKey", DeterministicGuid.Create($"cg-contest:{contestId}:{clientId}:{requestId}"));
                movementParams.Add("@CreatedBy", $"contest:{contestId}");
                movementParams.Add("@MovementId", dbType: DbType.Int64, direction: ParameterDirection.Output);

                await connection.ExecuteAsync(
                    "cg.sp_apply_wallet_movement",
                    movementParams,
                    tx,
                    commandType: CommandType.StoredProcedure);

                walletMovementId = movementParams.Get<long>("@MovementId");
            }

            var metadataJson = JsonSerializer.Serialize(new
            {
                selected = new
                {
                    slot = selectedSlot,
                    label = selectedReward.Label,
                    reward = selectedReward.RewardCandyCash
                },
                randomizedRewards = rewardPool
                    .Select((item, index) => new
                    {
                        displayIndex = index + 1,
                        item.Label,
                        item.RewardCandyCash
                    })
            }, JsonOptions);

            var playedAtUtc = await connection.ExecuteScalarAsync<DateTime>(@"
INSERT INTO cg.contest_plays
(
    contest_id,
    client_id,
    selected_slot,
    result_code,
    result_message,
    awarded_candycash,
    wallet_movement_id,
    client_request_id,
    source_ip,
    user_agent,
    metadata_json,
    played_at
)
VALUES
(
    @ContestId,
    @ClientId,
    @SelectedSlot,
    @ResultCode,
    @ResultMessage,
    @AwardedCandyCash,
    @WalletMovementId,
    @ClientRequestId,
    @SourceIp,
    @UserAgent,
    @MetadataJson,
    SYSUTCDATETIME()
);
SELECT CAST(SYSUTCDATETIME() AS DATETIME2(0));",
                new
                {
                    ContestId = contestId,
                    ClientId = clientId,
                    SelectedSlot = selectedSlot,
                    ResultCode = resultCode,
                    ResultMessage = resultMessage,
                    AwardedCandyCash = awarded,
                    WalletMovementId = walletMovementId,
                    ClientRequestId = requestId,
                    SourceIp = NormalizeOptional(sourceIp, 80),
                    UserAgent = NormalizeOptional(userAgent, 320),
                    MetadataJson = metadataJson
                },
                tx);

            var walletBalance = await GetWalletBalanceAsync(connection, tx, clientId);
            var remaining = Math.Max(0, contest.MaxPlaysPerClient - (usedPlays + 1));

            await tx.CommitAsync();

            return new ContestPlayResultDto
            {
                ContestId = contestId,
                SelectedSlot = selectedSlot,
                IsWinner = awarded > 0,
                AwardedCandyCash = awarded,
                ResultCode = resultCode,
                ResultMessage = resultMessage,
                WalletMovementId = walletMovementId,
                WalletBalance = walletBalance,
                RemainingPlays = remaining,
                PlayedAtUtc = playedAtUtc
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<AdminContestDto> GetAdminContestByIdAsync(long contestId)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<ContestAdminRow>(@"
SELECT
    c.id,
    c.contest_name,
    c.contest_slug,
    c.contest_type,
    c.description,
    c.icon_name,
    c.config_json,
    c.audience_type,
    c.max_plays_per_client,
    c.starts_at_utc,
    c.ends_at_utc,
    c.is_active,
    c.updated_at,
    c.updated_by,
    ISNULL(stats.total_plays, 0) AS total_plays,
    ISNULL(stats.total_winners, 0) AS total_winners
FROM cg.contests c
OUTER APPLY
(
    SELECT
        COUNT(1) AS total_plays,
        SUM(CASE WHEN cp.awarded_candycash > 0 THEN 1 ELSE 0 END) AS total_winners
    FROM cg.contest_plays cp
    WHERE cp.contest_id = c.id
) stats
WHERE c.id = @Id",
            new { Id = contestId });

        if (row is null)
        {
            throw new KeyNotFoundException("Concurso no encontrado.");
        }

        var targetClientIds = (await connection.QueryAsync<long>(@"
SELECT client_id
FROM cg.contest_targets
WHERE contest_id = @ContestId
  AND is_enabled = 1",
            new { ContestId = contestId })).ToList();

        return MapAdminContest(row, targetClientIds);
    }

    private static AdminContestDto MapAdminContest(ContestAdminRow row, IReadOnlyList<long> targetClientIds)
    {
        return new AdminContestDto
        {
            Id = row.Id,
            ContestName = row.ContestName,
            ContestSlug = row.ContestSlug,
            ContestType = row.ContestType,
            Description = row.Description,
            IconName = row.IconName,
            Config = ParseContestConfig(row.ConfigJson),
            AudienceType = row.AudienceType,
            MaxPlaysPerClient = row.MaxPlaysPerClient,
            StartsAtUtc = row.StartsAtUtc,
            EndsAtUtc = row.EndsAtUtc,
            IsActive = row.IsActive,
            UpdatedAt = row.UpdatedAt,
            UpdatedBy = row.UpdatedBy,
            TotalPlays = row.TotalPlays,
            TotalWinners = row.TotalWinners,
            TargetClientIds = targetClientIds
        };
    }

    private static ContestConfigDto ParseContestConfig(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new InvalidOperationException("El concurso no tiene configuración de cajas.");
        }

        ContestConfigDto? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ContestConfigDto>(rawJson, JsonOptions);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("La configuración JSON del concurso es inválida.");
        }

        if (parsed is null)
        {
            throw new InvalidOperationException("La configuración del concurso no pudo leerse.");
        }

        return NormalizeContestConfig(parsed);
    }

    private static ContestConfigDto NormalizeContestConfig(ContestConfigDto config)
    {
        var boxes = (config.Boxes ?? Array.Empty<ContestBoxDto>())
            .Where(box => box is not null)
            .Select(box => new ContestBoxDto
            {
                Slot = box.Slot,
                Label = NormalizeRequired(box.Label, "box.label", 80, 1),
                RewardCandyCash = decimal.Round(box.RewardCandyCash, 2)
            })
            .OrderBy(box => box.Slot)
            .ToList();

        if (boxes.Count < 2)
        {
            throw new ArgumentException("El concurso debe tener al menos 2 cajas.");
        }

        if (boxes.Count > 12)
        {
            throw new ArgumentException("El concurso soporta máximo 12 cajas.");
        }

        if (boxes.Any(box => box.Slot <= 0 || box.Slot > 12))
        {
            throw new ArgumentException("Los slots de cajas deben estar entre 1 y 12.");
        }

        if (boxes.GroupBy(box => box.Slot).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("No se permiten slots repetidos en las cajas.");
        }

        if (boxes.Any(box => box.RewardCandyCash < 0))
        {
            throw new ArgumentException("Las recompensas no pueden ser negativas.");
        }

        return new ContestConfigDto
        {
            Boxes = boxes,
            EmptyResultMessage = NormalizeOptional(config.EmptyResultMessage, 180) ?? "Sigue intentando, pronto te toca.",
            WinResultPrefix = NormalizeOptional(config.WinResultPrefix, 60) ?? "Ganaste"
        };
    }

    private static string BuildResultMessage(ContestConfigDto config, decimal reward)
    {
        if (reward <= 0m)
        {
            return NormalizeOptional(config.EmptyResultMessage, 180) ?? "Sigue intentando, pronto te toca.";
        }

        var prefix = NormalizeOptional(config.WinResultPrefix, 60) ?? "Ganaste";
        return $"{prefix} {reward.ToString("0.##", CultureInfo.InvariantCulture)} CC.";
    }

    private static void ShuffleRewards(List<ContestRewardCandidate> rewards)
    {
        for (var i = rewards.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (rewards[i], rewards[j]) = (rewards[j], rewards[i]);
        }
    }

    private async Task ReplaceTargetsAsync(
        System.Data.IDbConnection connection,
        IDbTransaction tx,
        long contestId,
        string audienceType,
        IReadOnlyList<long> targetClientIds)
    {
        await connection.ExecuteAsync(
            "DELETE FROM cg.contest_targets WHERE contest_id = @ContestId",
            new { ContestId = contestId },
            tx);

        if (audienceType != "CLIENTS")
        {
            return;
        }

        var filtered = targetClientIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (filtered.Length == 0)
        {
            return;
        }

        await connection.ExecuteAsync(@"
INSERT INTO cg.contest_targets (contest_id, client_id, is_enabled, created_at)
SELECT @ContestId,
       c.id,
       1,
       SYSUTCDATETIME()
FROM cg.clients c
WHERE c.id IN @ClientIds",
            new
            {
                ContestId = contestId,
                ClientIds = filtered
            },
            tx);
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

    private static IReadOnlyList<long> NormalizeClientIds(IReadOnlyList<long>? source)
    {
        return (source ?? Array.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    private static ContestInputNormalized NormalizeContestInput(
        string? contestName,
        string? contestSlug,
        string? description,
        string? iconName,
        string? audienceType,
        int maxPlaysPerClient,
        DateTime? startsAtUtc,
        DateTime? endsAtUtc,
        bool isActive,
        ContestConfigDto config,
        IReadOnlyList<long>? targetClientIds)
    {
        var normalizedName = NormalizeRequired(contestName, "contestName", 120, 3);
        var normalizedSlug = NormalizeSlug(contestSlug);
        var normalizedDescription = NormalizeOptional(description, 500);
        var normalizedIconName = NormalizeOptional(iconName, 80) ?? "gift-box";
        var normalizedAudience = NormalizeAudienceType(audienceType);

        if (maxPlaysPerClient <= 0 || maxPlaysPerClient > 20)
        {
            throw new ArgumentException("MaxPlaysPerClient debe estar entre 1 y 20.");
        }

        if (startsAtUtc.HasValue && endsAtUtc.HasValue && endsAtUtc.Value < startsAtUtc.Value)
        {
            throw new ArgumentException("EndsAtUtc no puede ser menor que StartsAtUtc.");
        }

        var normalizedConfig = NormalizeContestConfig(config ?? new ContestConfigDto());

        var targets = normalizedAudience == "CLIENTS"
            ? NormalizeClientIds(targetClientIds)
            : Array.Empty<long>();

        var configJson = JsonSerializer.Serialize(normalizedConfig, JsonOptions);

        return new ContestInputNormalized(
            normalizedName,
            normalizedSlug,
            normalizedDescription,
            normalizedIconName,
            normalizedAudience,
            maxPlaysPerClient,
            startsAtUtc,
            endsAtUtc,
            isActive,
            configJson,
            targets);
    }

    private static string NormalizeSlug(string? value)
    {
        var text = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("contestSlug es requerido.");
        }

        var chars = text.Select(ch =>
        {
            if (char.IsLetterOrDigit(ch))
            {
                return ch;
            }

            return ch switch
            {
                ' ' or '_' => '-',
                '-' => '-',
                _ => '\0'
            };
        }).Where(ch => ch != '\0').ToArray();

        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        normalized = normalized.Trim('-');
        if (normalized.Length < 3)
        {
            throw new ArgumentException("contestSlug inválido.");
        }

        if (normalized.Length > 90)
        {
            normalized = normalized[..90].Trim('-');
        }

        return normalized;
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

    private static void ValidateContestAvailability(ContestPlayRow contest)
    {
        if (!contest.IsActive)
        {
            throw new InvalidOperationException("Este concurso no está activo.");
        }

        var now = DateTime.UtcNow;
        if (contest.StartsAtUtc.HasValue && contest.StartsAtUtc.Value > now)
        {
            throw new InvalidOperationException("Este concurso aún no ha comenzado.");
        }

        if (contest.EndsAtUtc.HasValue && contest.EndsAtUtc.Value < now)
        {
            throw new InvalidOperationException("Este concurso ya finalizó.");
        }

        if (!string.Equals(contest.ContestType, "PICK_A_BOX", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Tipo de concurso no soportado.");
        }
    }

    private static async Task<decimal> GetWalletBalanceAsync(System.Data.IDbConnection connection, IDbTransaction tx, long clientId)
    {
        var balance = await connection.QuerySingleOrDefaultAsync<decimal?>(@"
SELECT balance
FROM cg.wallet_accounts
WHERE client_id = @ClientId",
            new { ClientId = clientId },
            tx);

        return balance ?? 0m;
    }

    private sealed record ContestRewardCandidate(string Label, decimal RewardCandyCash);

    private sealed record ContestInputNormalized(
        string ContestName,
        string ContestSlug,
        string? Description,
        string IconName,
        string AudienceType,
        int MaxPlaysPerClient,
        DateTime? StartsAtUtc,
        DateTime? EndsAtUtc,
        bool IsActive,
        string ConfigJson,
        IReadOnlyList<long> TargetClientIds);

    private sealed class ContestAdminRow
    {
        public long Id { get; set; }
        public string ContestName { get; set; } = string.Empty;
        public string ContestSlug { get; set; } = string.Empty;
        public string ContestType { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? IconName { get; set; }
        public string ConfigJson { get; set; } = string.Empty;
        public string AudienceType { get; set; } = "ALL";
        public int MaxPlaysPerClient { get; set; }
        public DateTime? StartsAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }
        public bool IsActive { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public int TotalPlays { get; set; }
        public int TotalWinners { get; set; }
    }

    private sealed class ContestTargetRow
    {
        public long ContestId { get; set; }
        public long ClientId { get; set; }
    }

    private sealed class ClientContestRow
    {
        public long Id { get; set; }
        public string ContestName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? IconName { get; set; }
        public string ConfigJson { get; set; } = string.Empty;
        public int MaxPlaysPerClient { get; set; }
        public int UsedPlays { get; set; }
        public DateTime? EndsAtUtc { get; set; }
    }

    private sealed class ContestPlayRow
    {
        public long Id { get; set; }
        public string ContestName { get; set; } = string.Empty;
        public string ContestType { get; set; } = string.Empty;
        public string ConfigJson { get; set; } = string.Empty;
        public string AudienceType { get; set; } = "ALL";
        public int MaxPlaysPerClient { get; set; }
        public DateTime? StartsAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class ContestExistingPlayRow
    {
        public long Id { get; set; }
        public int SelectedSlot { get; set; }
        public string ResultCode { get; set; } = string.Empty;
        public string? ResultMessage { get; set; }
        public decimal AwardedCandyCash { get; set; }
        public long? WalletMovementId { get; set; }
        public DateTime PlayedAt { get; set; }
    }
}
