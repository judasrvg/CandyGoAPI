using System.Data;
using CandyGo.Api.Domain;
using CandyGo.Api.DTOs;
using CandyGo.Api.Infrastructure;
using CandyGo.Api.Security;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CandyGo.Api.Services;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(long clientId, CreateOrderRequest request);
    Task<OrderDto?> GetByIdAsync(long orderId);
    Task<IReadOnlyList<OrderDto>> GetByClientIdAsync(long clientId, int take = 100);
    Task<IReadOnlyList<OrderDto>> GetAllAsync(int take = 300);
    Task<OrderDto> UpdateStatusAsync(long orderId, UpdateOrderStatusRequest request, string changedBy);
}

public sealed class OrderService : IOrderService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public OrderService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<OrderDto> CreateOrderAsync(long clientId, CreateOrderRequest request)
    {
        if (clientId <= 0)
        {
            throw new ArgumentException("ClientId inválido.");
        }

        var fulfillmentType = NormalizeFulfillmentType(request.FulfillmentType);
        var clientRequestId = request.ClientRequestId ?? Guid.NewGuid();

        if (fulfillmentType == "DELIVERY" && string.IsNullOrWhiteSpace(request.DeliveryAddress))
        {
            throw new ArgumentException("La dirección es requerida para pedidos con mensajería.");
        }

        var groupedItems = (request.Items ?? new List<CreateOrderItemRequest>())
            .GroupBy(x => x.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                Quantity = group.Sum(x => x.Quantity)
            })
            .Where(x => x.Quantity > 0)
            .ToList();

        if (groupedItems.Count == 0)
        {
            throw new ArgumentException("La orden debe incluir al menos un producto válido.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        var existingOrderId = await connection.QuerySingleOrDefaultAsync<long?>(@"
SELECT id
FROM cg.orders
WHERE client_id = @ClientId
  AND client_request_id = @ClientRequestId",
            new
            {
                ClientId = clientId,
                ClientRequestId = clientRequestId
            });

        if (existingOrderId.HasValue)
        {
            var existingOrder = await GetByIdAsync(existingOrderId.Value);
            if (existingOrder is null)
            {
                throw new InvalidOperationException("No se pudo recuperar la orden existente.");
            }

            return existingOrder;
        }

        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            var productIds = groupedItems.Select(x => x.ProductId).Distinct().ToArray();
            var dbProducts = (await connection.QueryAsync<ProductPriceRow>(@"
SELECT id,
       name,
       price_candycash,
       is_active
FROM cg.products
WHERE id IN @Ids",
                new { Ids = productIds },
                tx)).ToDictionary(x => x.Id);

            foreach (var item in groupedItems)
            {
                if (!dbProducts.TryGetValue(item.ProductId, out var product))
                {
                    throw new InvalidOperationException($"Producto {item.ProductId} no existe.");
                }

                if (!product.IsActive)
                {
                    throw new InvalidOperationException($"Producto {item.ProductId} no está activo.");
                }
            }

            var subtotal = groupedItems.Sum(item => dbProducts[item.ProductId].PriceCandyCash * item.Quantity);

            var rules = await connection.QuerySingleAsync<BusinessRuleRow>(@"
SELECT delivery_fee
FROM cg.business_rules
WHERE id = 1", transaction: tx);

            var deliveryFee = fulfillmentType == "DELIVERY" ? rules.DeliveryFee : 0m;
            var total = subtotal + deliveryFee;

            var movementParams = new DynamicParameters();
            movementParams.Add("@ClientId", clientId);
            movementParams.Add("@SignedAmount", -total);
            movementParams.Add("@Reason", "Pago de orden CandyGo");
            movementParams.Add("@SourceType", "ORDER_PAYMENT");
            movementParams.Add("@SourceRef", clientRequestId.ToString());
            movementParams.Add("@IdempotencyKey", clientRequestId);
            movementParams.Add("@CreatedBy", $"client:{clientId}");
            movementParams.Add("@MovementId", dbType: DbType.Int64, direction: ParameterDirection.Output);

            await connection.ExecuteAsync(
                "cg.sp_apply_wallet_movement",
                movementParams,
                tx,
                commandType: CommandType.StoredProcedure);

            var walletMovementId = movementParams.Get<long>("@MovementId");

            var orderId = await connection.ExecuteScalarAsync<long>(@"
INSERT INTO cg.orders
(
    client_id,
    status,
    fulfillment_type,
    subtotal_candycash,
    delivery_fee,
    total_candycash,
    delivery_address,
    notes,
    wallet_movement_id,
    client_request_id
)
VALUES
(
    @ClientId,
    @Status,
    @FulfillmentType,
    @Subtotal,
    @DeliveryFee,
    @Total,
    @DeliveryAddress,
    @Notes,
    @WalletMovementId,
    @ClientRequestId
);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                new
                {
                    ClientId = clientId,
                    Status = OrderStatusMachine.Pending,
                    FulfillmentType = fulfillmentType,
                    Subtotal = subtotal,
                    DeliveryFee = deliveryFee,
                    Total = total,
                    DeliveryAddress = string.IsNullOrWhiteSpace(request.DeliveryAddress) ? null : request.DeliveryAddress.Trim(),
                    Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                    WalletMovementId = walletMovementId,
                    ClientRequestId = clientRequestId
                },
                tx);

            foreach (var item in groupedItems)
            {
                var product = dbProducts[item.ProductId];
                await connection.ExecuteAsync(@"
INSERT INTO cg.order_items
(
    order_id,
    product_id,
    product_name_snapshot,
    unit_price_candycash,
    quantity
)
VALUES
(
    @OrderId,
    @ProductId,
    @ProductNameSnapshot,
    @UnitPriceCandyCash,
    @Quantity
)",
                    new
                    {
                        OrderId = orderId,
                        ProductId = product.Id,
                        ProductNameSnapshot = product.Name,
                        UnitPriceCandyCash = product.PriceCandyCash,
                        Quantity = item.Quantity
                    },
                    tx);
            }

            await connection.ExecuteAsync(@"
INSERT INTO cg.order_status_history
(
    order_id,
    from_status,
    to_status,
    changed_by,
    reason
)
VALUES
(
    @OrderId,
    NULL,
    @Status,
    @ChangedBy,
    @Reason
)",
                new
                {
                    OrderId = orderId,
                    Status = OrderStatusMachine.Pending,
                    ChangedBy = $"client:{clientId}",
                    Reason = "Orden creada"
                },
                tx);

            await tx.CommitAsync();

            var created = await GetByIdAsync(orderId);
            if (created is null)
            {
                throw new InvalidOperationException("No se pudo recuperar la orden creada.");
            }

            return created;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await tx.RollbackAsync();

            var existing = await connection.QuerySingleOrDefaultAsync<long?>(@"
SELECT id
FROM cg.orders
WHERE client_id = @ClientId
  AND client_request_id = @ClientRequestId",
                new
                {
                    ClientId = clientId,
                    ClientRequestId = clientRequestId
                });

            if (existing.HasValue)
            {
                var existingOrder = await GetByIdAsync(existing.Value);
                if (existingOrder is not null)
                {
                    return existingOrder;
                }
            }

            throw;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<OrderDto?> GetByIdAsync(long orderId)
    {
        await using var connection = _connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<OrderRow>(@"
SELECT o.id,
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
WHERE o.id = @OrderId",
            new { OrderId = orderId });

        if (row is null)
        {
            return null;
        }

        var items = await connection.QueryAsync<OrderItemDto>(@"
SELECT id,
       product_id,
       product_name_snapshot,
       unit_price_candycash,
       quantity,
       line_total_candycash
FROM cg.order_items
WHERE order_id = @OrderId
ORDER BY id ASC",
            new { OrderId = orderId });

        return MapOrder(row, items.ToList());
    }

    public async Task<IReadOnlyList<OrderDto>> GetByClientIdAsync(long clientId, int take = 100)
    {
        if (take <= 0 || take > 300)
        {
            take = 100;
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
WHERE o.client_id = @ClientId
ORDER BY o.created_at DESC, o.id DESC",
            new
            {
                Take = take,
                ClientId = clientId
            })).ToList();

        return await AttachItemsAsync(connection, rows);
    }

    public async Task<IReadOnlyList<OrderDto>> GetAllAsync(int take = 300)
    {
        if (take <= 0 || take > 500)
        {
            take = 300;
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
ORDER BY o.created_at DESC, o.id DESC",
            new { Take = take })).ToList();

        return await AttachItemsAsync(connection, rows);
    }

    public async Task<OrderDto> UpdateStatusAsync(long orderId, UpdateOrderStatusRequest request, string changedBy)
    {
        var nextStatus = NormalizeStatus(request.Status);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        try
        {
            var currentOrder = await connection.QuerySingleOrDefaultAsync<OrderStatusRow>(@"
SELECT id,
       client_id,
       status,
       total_candycash,
       wallet_movement_id
FROM cg.orders WITH (UPDLOCK, ROWLOCK)
WHERE id = @Id",
                new { Id = orderId },
                tx);

            if (currentOrder is null)
            {
                throw new KeyNotFoundException("Orden no encontrada.");
            }

            if (!OrderStatusMachine.CanTransition(currentOrder.Status, nextStatus))
            {
                throw new InvalidOperationException($"Transición de estado inválida: {currentOrder.Status} -> {nextStatus}");
            }

            if (!string.Equals(currentOrder.Status, nextStatus, StringComparison.Ordinal)
                && string.Equals(nextStatus, OrderStatusMachine.Cancelled, StringComparison.Ordinal)
                && currentOrder.TotalCandyCash > 0)
            {
                var refundIdempotency = DeterministicGuid.Create($"cg-order-refund:{currentOrder.Id}");

                var refundParams = new DynamicParameters();
                refundParams.Add("@ClientId", currentOrder.ClientId);
                refundParams.Add("@SignedAmount", currentOrder.TotalCandyCash);
                refundParams.Add("@Reason", "Reembolso por cancelación");
                refundParams.Add("@SourceType", "ORDER_CANCEL_REFUND");
                refundParams.Add("@SourceRef", currentOrder.Id.ToString());
                refundParams.Add("@IdempotencyKey", refundIdempotency);
                refundParams.Add("@CreatedBy", changedBy);
                refundParams.Add("@MovementId", dbType: DbType.Int64, direction: ParameterDirection.Output);

                await connection.ExecuteAsync(
                    "cg.sp_apply_wallet_movement",
                    refundParams,
                    tx,
                    commandType: CommandType.StoredProcedure);
            }

            await connection.ExecuteAsync(@"
UPDATE cg.orders
SET status = @Status,
    updated_at = SYSUTCDATETIME(),
    last_status_by = @ChangedBy,
    confirmed_at = CASE WHEN @Status = 'CONFIRMADA' AND confirmed_at IS NULL THEN SYSUTCDATETIME() ELSE confirmed_at END,
    delivered_at = CASE WHEN @Status = 'ENTREGADA' AND delivered_at IS NULL THEN SYSUTCDATETIME() ELSE delivered_at END,
    cancelled_at = CASE WHEN @Status = 'CANCELADA' AND cancelled_at IS NULL THEN SYSUTCDATETIME() ELSE cancelled_at END
WHERE id = @Id",
                new
                {
                    Id = orderId,
                    Status = nextStatus,
                    ChangedBy = changedBy
                },
                tx);

            await connection.ExecuteAsync(@"
INSERT INTO cg.order_status_history
(
    order_id,
    from_status,
    to_status,
    changed_by,
    reason
)
VALUES
(
    @OrderId,
    @FromStatus,
    @ToStatus,
    @ChangedBy,
    @Reason
)",
                new
                {
                    OrderId = orderId,
                    FromStatus = currentOrder.Status,
                    ToStatus = nextStatus,
                    ChangedBy = changedBy,
                    Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
                },
                tx);

            await tx.CommitAsync();

            var updated = await GetByIdAsync(orderId);
            if (updated is null)
            {
                throw new InvalidOperationException("No se pudo recuperar la orden actualizada.");
            }

            return updated;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static string NormalizeFulfillmentType(string raw)
    {
        var value = (raw ?? string.Empty).Trim().ToUpperInvariant();
        if (value is not ("PICKUP" or "DELIVERY"))
        {
            throw new ArgumentException("FulfillmentType inválido. Valores permitidos: PICKUP | DELIVERY.");
        }

        return value;
    }

    private static string NormalizeStatus(string raw)
    {
        var value = (raw ?? string.Empty).Trim().ToUpperInvariant();
        if (!OrderStatusMachine.IsValid(value))
        {
            throw new ArgumentException("Estado inválido.");
        }

        return value;
    }

    private async Task<IReadOnlyList<OrderDto>> AttachItemsAsync(SqlConnection connection, IReadOnlyList<OrderRow> rows)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<OrderDto>();
        }

        var orderIds = rows.Select(x => x.Id).Distinct().ToArray();

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

        var itemsByOrder = itemRows.GroupBy(x => x.OrderId)
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

        return rows.Select(row =>
        {
            itemsByOrder.TryGetValue(row.Id, out var orderItems);
            return MapOrder(row, orderItems ?? new List<OrderItemDto>());
        }).ToList();
    }

    private static OrderDto MapOrder(OrderRow row, IReadOnlyList<OrderItemDto> items)
    {
        return new OrderDto
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
            Items = items
        };
    }

    private sealed class ProductPriceRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal PriceCandyCash { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class BusinessRuleRow
    {
        public decimal DeliveryFee { get; set; }
    }

    private sealed class OrderStatusRow
    {
        public long Id { get; set; }
        public long ClientId { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal TotalCandyCash { get; set; }
        public long? WalletMovementId { get; set; }
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
