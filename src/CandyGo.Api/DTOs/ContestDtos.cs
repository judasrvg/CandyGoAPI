namespace CandyGo.Api.DTOs;

public sealed class ContestBoxDto
{
    public int Slot { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal RewardCandyCash { get; set; }
}

public sealed class ContestConfigDto
{
    public IReadOnlyList<ContestBoxDto> Boxes { get; set; } = Array.Empty<ContestBoxDto>();
    public string EmptyResultMessage { get; set; } = "Sigue intentando, pronto te toca.";
    public string WinResultPrefix { get; set; } = "Ganaste";
}

public sealed class AdminContestDto
{
    public long Id { get; set; }
    public string ContestName { get; set; } = string.Empty;
    public string ContestSlug { get; set; } = string.Empty;
    public string ContestType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public ContestConfigDto Config { get; set; } = new();
    public string AudienceType { get; set; } = "ALL";
    public int MaxPlaysPerClient { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public bool IsActive { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public int TotalPlays { get; set; }
    public int TotalWinners { get; set; }
    public IReadOnlyList<long> TargetClientIds { get; set; } = Array.Empty<long>();
}

public sealed class CreateContestRequest
{
    public string ContestName { get; set; } = string.Empty;
    public string ContestSlug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public string AudienceType { get; set; } = "ALL";
    public int MaxPlaysPerClient { get; set; } = 1;
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public bool IsActive { get; set; }
    public ContestConfigDto Config { get; set; } = new();
    public IReadOnlyList<long> TargetClientIds { get; set; } = Array.Empty<long>();
}

public sealed class UpdateContestRequest
{
    public string ContestName { get; set; } = string.Empty;
    public string ContestSlug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public string AudienceType { get; set; } = "ALL";
    public int MaxPlaysPerClient { get; set; } = 1;
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public bool IsActive { get; set; }
    public ContestConfigDto Config { get; set; } = new();
    public IReadOnlyList<long> TargetClientIds { get; set; } = Array.Empty<long>();
}

public sealed class UpdateContestTargetsRequest
{
    public IReadOnlyList<long> ClientIds { get; set; } = Array.Empty<long>();
}

public sealed class ContestPlayTraceDto
{
    public long Id { get; set; }
    public long ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;
    public int SelectedSlot { get; set; }
    public string ResultCode { get; set; } = string.Empty;
    public string? ResultMessage { get; set; }
    public decimal AwardedCandyCash { get; set; }
    public long? WalletMovementId { get; set; }
    public DateTime PlayedAt { get; set; }
}

public sealed class ClientContestBoxDto
{
    public int Slot { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class ClientContestDto
{
    public long Id { get; set; }
    public string ContestName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public int MaxPlaysPerClient { get; set; }
    public int UsedPlays { get; set; }
    public int RemainingPlays { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public IReadOnlyList<ClientContestBoxDto> Boxes { get; set; } = Array.Empty<ClientContestBoxDto>();
}

public sealed class PlayContestRequest
{
    public int SelectedSlot { get; set; }
    public Guid? ClientRequestId { get; set; }
}

public sealed class ContestPlayResultDto
{
    public long ContestId { get; set; }
    public int SelectedSlot { get; set; }
    public bool IsWinner { get; set; }
    public decimal AwardedCandyCash { get; set; }
    public string ResultCode { get; set; } = string.Empty;
    public string ResultMessage { get; set; } = string.Empty;
    public long? WalletMovementId { get; set; }
    public decimal WalletBalance { get; set; }
    public int RemainingPlays { get; set; }
    public DateTime PlayedAtUtc { get; set; }
}
