using System.Data;
using CandyGo.Api.DTOs;
using CandyGo.Api.Infrastructure;
using Dapper;

namespace CandyGo.Api.Services;

public interface ISyncService
{
    Task<SyncOrdersResponse> PullOrdersAsync(DateTime? updatedAfterUtc, long? lastOrderId, int take);
    Task<SyncClientsResponse> PullClientsAsync(DateTime? updatedAfterUtc, int take);
    Task<SyncOrderStatusBatchResponse> PushOrderStatusesAsync(SyncOrderStatusBatchRequest request, string changedBy);
    Task<SyncReservationRewardBatchResponse> ApplyReservationRewardsAsync(SyncReservationRewardBatchRequest request, string changedBy);
}

public sealed class SyncService : ISyncService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IOrderService _orderService;

    public SyncService(IDbConnectionFactory connectionFactory, IOrderService orderService)
    {
        _connectionFactory = connectionFactory;
        _orderService = orderService;
    }

    public async Task<SyncOrdersResponse> PullOrdersAsync(DateTime? updatedAfterUtc, long? lastOrderId, int take)
    {
        if (take <= 0 || take > 500)
        {
            take = 200;
        }

        await using var connection = _connectionFactory.CreateConnection();

        var rows = (await connection.QueryAsync<OrderRow>(@"
SELECT TOP (@Take)
       o.id,
       o.client_id,
       c.full_name AS client_name,
       c.phone AS client_phone,
       o.status,
       o.fulfillment_type,
       o.subtotal_candycash,
       o.delivery_fee,
       o.total_candycash,
       o.delivery_address,
       o.notes,
       o.created_at,
       o.updated_at,
       o.confirmed_at,
       o.delivered_at,
       o.cancelled_at,
       o.wallet_movement_id
FROM cg.orders o
INNER JOIN cg.clients c ON c.id = o.client_id
WHERE (
       @UpdatedAfterUtc IS NULL
       OR o.updated_at > @UpdatedAfterUtc
       OR (o.updated_at = @UpdatedAfterUtc AND o.id > ISNULL(@LastOrderId, 0))
      )
ORDER BY o.updated_at ASC, o.id ASC",
            new
            {
                Take = take,
                UpdatedAfterUtc = updatedAfterUtc,
                LastOrderId = lastOrderId
            })).ToList();

        if (rows.Count == 0)
        {
            return new SyncOrdersResponse
            {
                Orders = Array.Empty<OrderDto>(),
                NextUpdatedAfterUtc = updatedAfterUtc,
                NextOrderId = lastOrderId
            };
        }

        var orderIds = rows.Select(x => x.Id).ToArray();
        var itemRows = await connection.QueryAsync<OrderItemRow>(@"
SELECT id,
       order_id,
       product_id,
       product_name_snapshot,
       unit_price_candycash,
       quantity,
       line_total_candycash
FROM cg.order_items
WHERE order_id IN @OrderIds
ORDER BY id ASC",
            new { OrderIds = orderIds });

        var itemsByOrder = itemRows
            .GroupBy(x => x.OrderId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(item => new OrderItemDto
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    ProductNameSnapshot = item.ProductNameSnapshot,
                    UnitPriceCandyCash = item.UnitPriceCandyCash,
                    Quantity = item.Quantity,
                    LineTotalCandyCash = item.LineTotalCandyCash
                }).ToList());

        var orders = rows.Select(row => new OrderDto
        {
            Id = row.Id,
            ClientId = row.ClientId,
            ClientName = row.ClientName,
            ClientPhone = row.ClientPhone,
            Status = row.Status,
            FulfillmentType = row.FulfillmentType,
            SubtotalCandyCash = row.SubtotalCandyCash,
            DeliveryFee = row.DeliveryFee,
            TotalCandyCash = row.TotalCandyCash,
            DeliveryAddress = row.DeliveryAddress,
            Notes = row.Notes,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            ConfirmedAt = row.ConfirmedAt,
            DeliveredAt = row.DeliveredAt,
            CancelledAt = row.CancelledAt,
            WalletMovementId = row.WalletMovementId,
            Items = itemsByOrder.TryGetValue(row.Id, out var orderItems)
                ? orderItems
                : Array.Empty<OrderItemDto>()
        }).ToList();

        var last = rows[^1];

        return new SyncOrdersResponse
        {
            Orders = orders,
            NextUpdatedAfterUtc = last.UpdatedAt,
            NextOrderId = last.Id
        };
    }

    public async Task<SyncClientsResponse> PullClientsAsync(DateTime? updatedAfterUtc, int take)
    {
        if (take <= 0 || take > 500)
        {
            take = 200;
        }

        await using var connection = _connectionFactory.CreateConnection();

        var clients = (await connection.QueryAsync<ClientDto>(@"
SELECT TOP (@Take)
       id,
       full_name,
       phone,
       is_active,
       source_system,
       created_at,
       updated_at
FROM cg.clients
WHERE @UpdatedAfterUtc IS NULL
   OR updated_at > @UpdatedAfterUtc
ORDER BY updated_at ASC, id ASC",
            new
            {
                Take = take,
                UpdatedAfterUtc = updatedAfterUtc
            })).ToList();

        return new SyncClientsResponse
        {
            Clients = clients,
            NextUpdatedAfterUtc = clients.Count > 0 ? clients[^1].UpdatedAt : updatedAfterUtc
        };
    }

    public async Task<SyncOrderStatusBatchResponse> PushOrderStatusesAsync(SyncOrderStatusBatchRequest request, string changedBy)
    {
        var results = new List<SyncOrderStatusItemResult>();

        foreach (var item in request.Items)
        {
            try
            {
                await _orderService.UpdateStatusAsync(item.OrderId, new UpdateOrderStatusRequest
                {
                    Status = item.NewStatus,
                    Reason = item.Reason
                }, changedBy);

                results.Add(new SyncOrderStatusItemResult
                {
                    OrderId = item.OrderId,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                results.Add(new SyncOrderStatusItemResult
                {
                    OrderId = item.OrderId,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        return new SyncOrderStatusBatchResponse { Items = results };
    }

    public async Task<SyncReservationRewardBatchResponse> ApplyReservationRewardsAsync(SyncReservationRewardBatchRequest request, string changedBy)
    {
        var source = string.IsNullOrWhiteSpace(request.ExternalSource)
            ? "PHOTOSTUDIO"
            : request.ExternalSource.Trim().ToUpperInvariant();

        var results = new List<SyncReservationRewardItemResult>();

        await using var connection = _connectionFactory.CreateConnection();

        foreach (var item in request.Items)
        {
            try
            {
                var eventId = item.ExternalEventId == Guid.Empty ? Guid.NewGuid() : item.ExternalEventId;

                var parameters = new DynamicParameters();
                parameters.Add("@ExternalSource", source);
                parameters.Add("@ExternalEventId", eventId);
                parameters.Add("@ReservationId", item.ReservationId <= 0 ? null : item.ReservationId);
                parameters.Add("@ClientPhone", item.ClientPhone);
                parameters.Add("@ReservationTotal", item.ReservationTotal);
                parameters.Add("@CreatedBy", changedBy);
                parameters.Add("@RewardMovementId", dbType: DbType.Int64, direction: ParameterDirection.Output);

                await connection.ExecuteAsync(
                    "cg.sp_apply_reservation_reward",
                    parameters,
                    commandType: CommandType.StoredProcedure);

                var movementId = parameters.Get<long>("@RewardMovementId");

                results.Add(new SyncReservationRewardItemResult
                {
                    ExternalEventId = eventId,
                    Success = true,
                    WalletMovementId = movementId
                });
            }
            catch (Exception ex)
            {
                results.Add(new SyncReservationRewardItemResult
                {
                    ExternalEventId = item.ExternalEventId,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        return new SyncReservationRewardBatchResponse { Items = results };
    }

    private sealed class OrderRow
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
    }

    private sealed class OrderItemRow
    {
        public long Id { get; set; }
        public long OrderId { get; set; }
        public long? ProductId { get; set; }
        public string ProductNameSnapshot { get; set; } = string.Empty;
        public decimal UnitPriceCandyCash { get; set; }
        public int Quantity { get; set; }
        public decimal LineTotalCandyCash { get; set; }
    }
}
