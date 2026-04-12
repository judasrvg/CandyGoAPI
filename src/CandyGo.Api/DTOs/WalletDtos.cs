namespace CandyGo.Api.DTOs;

public sealed class WalletSummaryDto
{
    public long ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public DateTime WalletUpdatedAt { get; set; }
    public IReadOnlyList<WalletMovementDto> Movements { get; set; } = Array.Empty<WalletMovementDto>();
}

public sealed class WalletMovementDto
{
    public long Id { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal SignedAmount { get; set; }
    public string? Reason { get; set; }
    public string? SourceType { get; set; }
    public string? SourceRef { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AdminAdjustWalletRequest
{
    public decimal Amount { get; set; }
    public string Operation { get; set; } = "CREDIT";
    public string? Reason { get; set; }
    public string? SourceRef { get; set; }
    public Guid? IdempotencyKey { get; set; }
}

public sealed class WalletAdjustmentResultDto
{
    public long ClientId { get; set; }
    public long MovementId { get; set; }
    public decimal SignedAmount { get; set; }
    public decimal Balance { get; set; }
    public WalletMovementDto? Movement { get; set; }
}
