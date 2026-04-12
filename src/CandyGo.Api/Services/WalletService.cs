using System.Data;
using CandyGo.Api.DTOs;
using CandyGo.Api.Infrastructure;
using Dapper;

namespace CandyGo.Api.Services;

public interface IWalletService
{
    Task<WalletSummaryDto> GetByClientIdAsync(long clientId, int movementLimit = 30);
    Task<WalletAdjustmentResultDto> AdjustByAdminAsync(long clientId, AdminAdjustWalletRequest request, string changedBy);
}

public sealed class WalletService : IWalletService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public WalletService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<WalletSummaryDto> GetByClientIdAsync(long clientId, int movementLimit = 30)
    {
        if (movementLimit <= 0 || movementLimit > 200)
        {
            movementLimit = 30;
        }

        await using var connection = _connectionFactory.CreateConnection();

        var summary = await connection.QuerySingleOrDefaultAsync<WalletSummaryDto>(@"
SELECT c.id AS client_id,
       c.full_name AS client_name,
       c.phone,
       ISNULL(wa.balance, 0) AS balance,
       ISNULL(wa.updated_at, c.updated_at) AS wallet_updated_at
FROM cg.clients c
LEFT JOIN cg.wallet_accounts wa ON wa.client_id = c.id
WHERE c.id = @ClientId",
            new { ClientId = clientId });

        if (summary is null)
        {
            throw new KeyNotFoundException("Cliente no encontrado.");
        }

        var movements = await connection.QueryAsync<WalletMovementDto>(@"
SELECT TOP (@Limit)
       wm.id,
       wm.movement_type,
       wm.amount,
       wm.signed_amount,
       wm.reason,
       wm.source_type,
       wm.source_ref,
       wm.created_at
FROM cg.wallet_movements wm
INNER JOIN cg.wallet_accounts wa ON wa.id = wm.wallet_account_id
WHERE wa.client_id = @ClientId
ORDER BY wm.id DESC",
            new
            {
                Limit = movementLimit,
                ClientId = clientId
            });

        summary.Balance = Math.Max(0, summary.Balance);
        summary.Movements = movements.ToList();

        return summary;
    }

    public async Task<WalletAdjustmentResultDto> AdjustByAdminAsync(long clientId, AdminAdjustWalletRequest request, string changedBy)
    {
        if (clientId <= 0)
        {
            throw new ArgumentException("ClientId inválido.");
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentException("El monto debe ser mayor que 0.");
        }

        var operation = NormalizeOperation(request.Operation);
        var signedAmount = operation == "DEBIT" ? -request.Amount : request.Amount;
        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? $"Ajuste manual admin ({operation})"
            : request.Reason.Trim();

        var idempotencyKey = request.IdempotencyKey == Guid.Empty ? null : request.IdempotencyKey;
        var sourceRef = string.IsNullOrWhiteSpace(request.SourceRef)
            ? null
            : request.SourceRef.Trim();

        await using var connection = _connectionFactory.CreateConnection();

        var exists = await connection.QuerySingleOrDefaultAsync<long?>(
            "SELECT id FROM cg.clients WHERE id = @ClientId",
            new { ClientId = clientId });

        if (!exists.HasValue)
        {
            throw new KeyNotFoundException("Cliente no encontrado.");
        }

        var parameters = new DynamicParameters();
        parameters.Add("@ClientId", clientId);
        parameters.Add("@SignedAmount", signedAmount);
        parameters.Add("@Reason", reason);
        parameters.Add("@SourceType", "ADMIN_ADJUSTMENT");
        parameters.Add("@SourceRef", sourceRef);
        parameters.Add("@IdempotencyKey", idempotencyKey);
        parameters.Add("@CreatedBy", string.IsNullOrWhiteSpace(changedBy) ? "admin" : changedBy.Trim());
        parameters.Add("@MovementId", dbType: DbType.Int64, direction: ParameterDirection.Output);

        await connection.ExecuteAsync(
            "cg.sp_apply_wallet_movement",
            parameters,
            commandType: CommandType.StoredProcedure);

        var movementId = parameters.Get<long>("@MovementId");
        var wallet = await GetByClientIdAsync(clientId, movementLimit: 40);
        var movement = wallet.Movements.FirstOrDefault(x => x.Id == movementId);

        return new WalletAdjustmentResultDto
        {
            ClientId = clientId,
            MovementId = movementId,
            SignedAmount = signedAmount,
            Balance = wallet.Balance,
            Movement = movement
        };
    }

    private static string NormalizeOperation(string operation)
    {
        var value = (operation ?? string.Empty).Trim().ToUpperInvariant();
        return value switch
        {
            "CREDIT" => "CREDIT",
            "DEBIT" => "DEBIT",
            _ => throw new ArgumentException("La operación debe ser CREDIT o DEBIT.")
        };
    }
}
