namespace CandyGo.Api.Security;

public interface ITokenService
{
    TokenResult CreateToken(long userId, string phone, string displayName, string role);
}

public sealed record TokenResult(string Token, DateTime ExpiresAtUtc);
