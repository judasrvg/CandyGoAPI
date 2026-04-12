using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CandyGo.Api.Security;

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;

    public TokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public TokenResult CreateToken(long userId, string phone, string displayName, string role)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_options.ExpiresMinutes <= 0 ? 120 : _options.ExpiresMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.UniqueName, phone),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, phone),
            new("display_name", displayName),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: creds);

        var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenResult(tokenValue, expiresAt);
    }
}
