using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CandyGo.Api.Security;

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _signingKey;

    public TokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        var keyBytes = Encoding.UTF8.GetBytes(_options.Key ?? string.Empty);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key debe tener al menos 32 bytes (256 bits).");
        }

        _signingKey = new SymmetricSecurityKey(keyBytes);
    }

    public TokenResult CreateToken(long userId, string phone, string displayName, string role)
    {
        var issuedAt = DateTime.UtcNow;
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.ExpiresMinutes <= 0 ? 120 : _options.ExpiresMinutes);
        var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(issuedAt).ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.UniqueName, phone),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, phone),
            new("display_name", displayName),
            new(ClaimTypes.Role, role)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: creds);

        var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenResult(tokenValue, expiresAt);
    }
}
