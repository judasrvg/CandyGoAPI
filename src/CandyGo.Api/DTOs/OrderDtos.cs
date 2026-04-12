namespace CandyGo.Api.DTOs;

public sealed class CreateOrderRequest
{
    public Guid? ClientRequestId { get; set; }
    public string FulfillmentType { get; set; } = "PICKUP";
    public string? DeliveryAddress { get; set; }
    public string? Notes { get; set; }
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}

public sealed class CreateOrderItemRequest
{
    public long ProductId { get; set; }
    public int Quantity { get; set; }
}

public sealed class UpdateOrderStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public sealed class OrderDto
{
    public long Id { get; set; }
    public long ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string ClientPhone { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FulfillmentType { get; set; } = string.Empty;
    public decimal SubtotalCandyCash { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal TotalCandyCash { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public long? WalletMovementId { get; set; }
    public IReadOnlyList<OrderItemDto> Items { get; set; } = Array.Empty<OrderItemDto>();
}

public sealed class OrderItemDto
{
    public long Id { get; set; }
    public long? ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal UnitPriceCandyCash { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotalCandyCash { get; set; }
}
