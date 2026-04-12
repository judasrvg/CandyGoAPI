using CandyGo.Api.DTOs;
using CandyGo.Api.Infrastructure;
using CandyGo.Api.Security;
using Dapper;

namespace CandyGo.Api.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterClientAsync(RegisterClientRequest request);
    Task<AuthResponse> LoginClientAsync(ClientLoginRequest request);
    Task<AuthResponse> LoginAdminAsync(AdminLoginRequest request);
    Task<AuthResponse> BootstrapAdminAsync(BootstrapAdminRequest request, string bootstrapKey);
}

public sealed class AuthService : IAuthService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;

    public AuthService(
        IDbConnectionFactory connectionFactory,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IConfiguration configuration)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _configuration = configuration;
    }

    public async Task<AuthResponse> RegisterClientAsync(RegisterClientRequest request)
    {
        ValidatePassword(request.Password);

        var normalizedPhone = PhoneNormalizer.Normalize(request.Phone);
        var fullName = NormalizeName(request.FullName);

        _passwordHasher.CreateHash(request.Password, out var hash, out var salt);

        await using var connection = _connectionFactory.CreateConnection();

        var existingClientId = await connection.QuerySingleOrDefaultAsync<long?>(
            "SELECT id FROM cg.clients WHERE phone = @Phone",
            new { Phone = normalizedPhone });

        if (existingClientId.HasValue)
        {
            throw new InvalidOperationException("Ya existe un cliente con ese teléfono.");
        }

        var clientId = await connection.ExecuteScalarAsync<long>(@"
INSERT INTO cg.clients (full_name, phone, password_hash, password_salt, source_system)
VALUES (@FullName, @Phone, @PasswordHash, @PasswordSalt, @SourceSystem);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new
            {
                FullName = fullName,
                Phone = normalizedPhone,
                PasswordHash = hash,
                PasswordSalt = salt,
                SourceSystem = "CANDYGO"
            });

        await connection.ExecuteAsync(@"
INSERT INTO cg.wallet_accounts (client_id, balance)
VALUES (@ClientId, 0);",
            new { ClientId = clientId });

        var tokenResult = _tokenService.CreateToken(clientId, normalizedPhone, fullName, "client");

        return new AuthResponse
        {
            Token = tokenResult.Token,
            ExpiresAtUtc = tokenResult.ExpiresAtUtc,
            User = new UserProfileDto
            {
                Id = clientId,
                FullName = fullName,
                Phone = normalizedPhone,
                Role = "client"
            }
        };
    }

    public async Task<AuthResponse> LoginClientAsync(ClientLoginRequest request)
    {
        var normalizedPhone = PhoneNormalizer.Normalize(request.Phone);

        await using var connection = _connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<ClientCredentialRow>(@"
SELECT id,
       full_name,
       phone,
       password_hash,
       password_salt,
       is_active
FROM cg.clients
WHERE phone = @Phone",
            new { Phone = normalizedPhone });

        if (row is null || !row.IsActive)
        {
            throw new UnauthorizedAccessException("Credenciales inválidas.");
        }

        if (!_passwordHasher.Verify(request.Password, row.PasswordHash, row.PasswordSalt))
        {
            throw new UnauthorizedAccessException("Credenciales inválidas.");
        }

        await connection.ExecuteAsync(
            "UPDATE cg.clients SET last_login_at = SYSUTCDATETIME(), updated_at = SYSUTCDATETIME() WHERE id = @Id",
            new { row.Id });

        var tokenResult = _tokenService.CreateToken(row.Id, row.Phone, row.FullName, "client");

        return new AuthResponse
        {
            Token = tokenResult.Token,
            ExpiresAtUtc = tokenResult.ExpiresAtUtc,
            User = new UserProfileDto
            {
                Id = row.Id,
                FullName = row.FullName,
                Phone = row.Phone,
                Role = "client"
            }
        };
    }

    public async Task<AuthResponse> LoginAdminAsync(AdminLoginRequest request)
    {
        var normalizedPhone = PhoneNormalizer.Normalize(request.Phone);

        await using var connection = _connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<AdminCredentialRow>(@"
SELECT id,
       full_name,
       phone,
       password_hash,
       password_salt,
       role_name,
       is_active
FROM cg.admin_users
WHERE phone = @Phone",
            new { Phone = normalizedPhone });

        if (row is null || !row.IsActive)
        {
            throw new UnauthorizedAccessException("Credenciales inválidas.");
        }

        if (!_passwordHasher.Verify(request.Password, row.PasswordHash, row.PasswordSalt))
        {
            throw new UnauthorizedAccessException("Credenciales inválidas.");
        }

        await connection.ExecuteAsync(
            "UPDATE cg.admin_users SET last_login_at = SYSUTCDATETIME(), updated_at = SYSUTCDATETIME() WHERE id = @Id",
            new { row.Id });

        var tokenResult = _tokenService.CreateToken(row.Id, row.Phone, row.FullName, "admin");

        return new AuthResponse
        {
            Token = tokenResult.Token,
            ExpiresAtUtc = tokenResult.ExpiresAtUtc,
            User = new UserProfileDto
            {
                Id = row.Id,
                FullName = row.FullName,
                Phone = row.Phone,
                Role = "admin"
            }
        };
    }

    public async Task<AuthResponse> BootstrapAdminAsync(BootstrapAdminRequest request, string bootstrapKey)
    {
        var expectedBootstrapKey = _configuration["Sync:ApiKey"];
        if (string.IsNullOrWhiteSpace(expectedBootstrapKey) || !string.Equals(expectedBootstrapKey, bootstrapKey, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Bootstrap key inválida.");
        }

        ValidatePassword(request.Password);

        var normalizedPhone = PhoneNormalizer.Normalize(request.Phone);
        var fullName = NormalizeName(request.FullName);

        _passwordHasher.CreateHash(request.Password, out var hash, out var salt);

        await using var connection = _connectionFactory.CreateConnection();

        var existingAdminId = await connection.QuerySingleOrDefaultAsync<long?>(
            "SELECT id FROM cg.admin_users WHERE phone = @Phone",
            new { Phone = normalizedPhone });

        if (existingAdminId.HasValue)
        {
            throw new InvalidOperationException("Ya existe un admin con ese teléfono.");
        }

        var adminId = await connection.ExecuteScalarAsync<long>(@"
INSERT INTO cg.admin_users (full_name, phone, password_hash, password_salt, role_name)
VALUES (@FullName, @Phone, @PasswordHash, @PasswordSalt, @RoleName);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new
            {
                FullName = fullName,
                Phone = normalizedPhone,
                PasswordHash = hash,
                PasswordSalt = salt,
                RoleName = "ADMIN"
            });

        var tokenResult = _tokenService.CreateToken(adminId, normalizedPhone, fullName, "admin");

        return new AuthResponse
        {
            Token = tokenResult.Token,
            ExpiresAtUtc = tokenResult.ExpiresAtUtc,
            User = new UserProfileDto
            {
                Id = adminId,
                FullName = fullName,
                Phone = normalizedPhone,
                Role = "admin"
            }
        };
    }

    private static string NormalizeName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Nombre requerido.");
        }

        return fullName.Trim();
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            throw new ArgumentException("La contraseña debe tener al menos 6 caracteres.");
        }
    }

    private sealed class ClientCredentialRow
    {
        public long Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
        public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
        public bool IsActive { get; set; }
    }

    private sealed class AdminCredentialRow
    {
        public long Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
        public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
        public string RoleName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
