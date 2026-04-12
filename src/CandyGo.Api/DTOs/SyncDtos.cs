namespace CandyGo.Api.DTOs;

public sealed class SyncOrdersResponse
{
    public IReadOnlyList<OrderDto> Orders { get; set; } = Array.Empty<OrderDto>();
    public DateTime? NextUpdatedAfterUtc { get; set; }
    public long? NextOrderId { get; set; }
}

public sealed class SyncClientsResponse
{
    public IReadOnlyList<ClientDto> Clients { get; set; } = Array.Empty<ClientDto>();
    public DateTime? NextUpdatedAfterUtc { get; set; }
}

public sealed class SyncOrderStatusBatchRequest
{
    public List<SyncOrderStatusItem> Items { get; set; } = new();
}

public sealed class SyncOrderStatusItem
{
    public long OrderId { get; set; }
    public string NewStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public Guid? IdempotencyKey { get; set; }
}

public sealed class SyncOrderStatusItemResult
{
    public long OrderId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public sealed class SyncOrderStatusBatchResponse
{
    public IReadOnlyList<SyncOrderStatusItemResult> Items { get; set; } = Array.Empty<SyncOrderStatusItemResult>();
}

public sealed class SyncReservationRewardBatchRequest
{
    public string ExternalSource { get; set; } = "PHOTOSTUDIO";
    public List<SyncReservationRewardItem> Items { get; set; } = new();
}

public sealed class SyncReservationRewardItem
{
    public Guid ExternalEventId { get; set; }
    public long ReservationId { get; set; }
    public string ClientPhone { get; set; } = string.Empty;
    public decimal ReservationTotal { get; set; }
}

public sealed class SyncReservationRewardItemResult
{
    public Guid ExternalEventId { get; set; }
    public bool Success { get; set; }
    public long? WalletMovementId { get; set; }
    public string? Error { get; set; }
}

public sealed class SyncReservationRewardBatchResponse
{
    public IReadOnlyList<SyncReservationRewardItemResult> Items { get; set; } = Array.Empty<SyncReservationRewardItemResult>();
}
