using CandyGo.Api.DTOs;
using CandyGo.Api.Infrastructure;
using CandyGo.Api.Security;
using Dapper;

namespace CandyGo.Api.Services;

public interface IClientService
{
    Task<IReadOnlyList<ClientDto>> GetAllAsync();
    Task<ClientDto?> GetByIdAsync(long id);
    Task<ClientDto?> GetByPhoneAsync(string phone);
    Task<ClientDto> CreateByAdminAsync(CreateClientByAdminRequest request);
    Task<ClientDeleteResultDto> DeleteOrDeactivateAsync(long id);
}

public sealed class ClientService : IClientService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;

    public ClientService(IDbConnectionFactory connectionFactory, IPasswordHasher passwordHasher)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
    }

    public async Task<IReadOnlyList<ClientDto>> GetAllAsync()
    {
        await using var connection = _connectionFactory.CreateConnection();

        var clients = await connection.QueryAsync<ClientDto>(@"
SELECT c.id,
       full_name,
       phone,
       is_active,
       ISNULL(wa.balance, 0) AS balance,
       source_system,
       c.created_at,
       c.updated_at
FROM cg.clients c
LEFT JOIN cg.wallet_accounts wa ON wa.client_id = c.id
ORDER BY c.updated_at DESC, c.id DESC");

        return clients.ToList();
    }

    public async Task<ClientDto?> GetByIdAsync(long id)
    {
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<ClientDto>(@"
SELECT c.id,
       c.full_name,
       c.phone,
       c.is_active,
       ISNULL(wa.balance, 0) AS balance,
       c.source_system,
       c.created_at,
       c.updated_at
FROM cg.clients c
LEFT JOIN cg.wallet_accounts wa ON wa.client_id = c.id
WHERE c.id = @Id",
            new { Id = id });
    }

    public async Task<ClientDto?> GetByPhoneAsync(string phone)
    {
        var normalizedPhone = PhoneNormalizer.Normalize(phone);
        await using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<ClientDto>(@"
SELECT c.id,
       c.full_name,
       c.phone,
       c.is_active,
       ISNULL(wa.balance, 0) AS balance,
       c.source_system,
       c.created_at,
       c.updated_at
FROM cg.clients c
LEFT JOIN cg.wallet_accounts wa ON wa.client_id = c.id
WHERE c.phone = @Phone",
            new { Phone = normalizedPhone });
    }

    public async Task<ClientDto> CreateByAdminAsync(CreateClientByAdminRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ArgumentException("Nombre requerido.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
        {
            throw new ArgumentException("La contraseña debe tener al menos 6 caracteres.");
        }

        var normalizedPhone = PhoneNormalizer.Normalize(request.Phone);

        _passwordHasher.CreateHash(request.Password, out var hash, out var salt);

        await using var connection = _connectionFactory.CreateConnection();

        var existingClientId = await connection.QuerySingleOrDefaultAsync<long?>(
            "SELECT id FROM cg.clients WHERE phone = @Phone",
            new { Phone = normalizedPhone });

        if (existingClientId.HasValue)
        {
            throw new InvalidOperationException("Ya existe un cliente con ese teléfono.");
        }

        var newClientId = await connection.ExecuteScalarAsync<long>(@"
INSERT INTO cg.clients (full_name, phone, password_hash, password_salt, source_system)
VALUES (@FullName, @Phone, @PasswordHash, @PasswordSalt, @SourceSystem);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new
            {
                FullName = request.FullName.Trim(),
                Phone = normalizedPhone,
                PasswordHash = hash,
                PasswordSalt = salt,
                SourceSystem = request.SourceSystem?.Trim()
            });

        await connection.ExecuteAsync(
            "INSERT INTO cg.wallet_accounts (client_id, balance) VALUES (@ClientId, 0)",
            new { ClientId = newClientId });

        var created = await GetByIdAsync(newClientId);
        if (created is null)
        {
            throw new InvalidOperationException("No se pudo recuperar el cliente creado.");
        }

        return created;
    }

    public async Task<ClientDeleteResultDto> DeleteOrDeactivateAsync(long id)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var probe = await connection.QuerySingleOrDefaultAsync<ClientDeleteProbeRow>(@"
SELECT c.id,
       c.full_name,
       c.is_active,
       wa.id AS wallet_account_id
FROM cg.clients c
LEFT JOIN cg.wallet_accounts wa ON wa.client_id = c.id
WHERE c.id = @Id",
            new { Id = id },
            transaction);

        if (probe is null)
        {
            throw new KeyNotFoundException("Cliente no encontrado.");
        }

        var ordersCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM cg.orders WHERE client_id = @Id",
            new { Id = id },
            transaction);

        var walletMovementsCount = await connection.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM cg.wallet_movements wm
INNER JOIN cg.wallet_accounts wa ON wa.id = wm.wallet_account_id
WHERE wa.client_id = @Id",
            new { Id = id },
            transaction);

        var hadDependencies = ordersCount > 0 || walletMovementsCount > 0;
        if (hadDependencies)
        {
            if (probe.IsActive)
            {
                await connection.ExecuteAsync(
                    "UPDATE cg.clients SET is_active = 0, updated_at = SYSUTCDATETIME() WHERE id = @Id",
                    new { Id = id },
                    transaction);
            }

            await transaction.CommitAsync();

            return new ClientDeleteResultDto
            {
                ClientId = id,
                Action = "DEACTIVATED",
                HadDependencies = true,
                IsActive = false,
                OrdersCount = ordersCount,
                WalletMovementsCount = walletMovementsCount,
                Message = probe.IsActive
                    ? $"Cliente desactivado por dependencias ({ordersCount} órdenes, {walletMovementsCount} movimientos)."
                    : $"Cliente ya estaba desactivado y mantiene dependencias ({ordersCount} órdenes, {walletMovementsCount} movimientos)."
            };
        }

        if (probe.WalletAccountId.HasValue)
        {
            await connection.ExecuteAsync(
                "DELETE FROM cg.wallet_accounts WHERE client_id = @Id",
                new { Id = id },
                transaction);
        }

        await connection.ExecuteAsync(
            "DELETE FROM cg.clients WHERE id = @Id",
            new { Id = id },
            transaction);

        await transaction.CommitAsync();

        return new ClientDeleteResultDto
        {
            ClientId = id,
            Action = "DELETED",
            HadDependencies = false,
            IsActive = false,
            OrdersCount = 0,
            WalletMovementsCount = 0,
            Message = "Cliente eliminado definitivamente."
        };
    }

    private sealed class ClientDeleteProbeRow
    {
        public long Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public long? WalletAccountId { get; set; }
    }
}
