namespace CandyGo.Api.Security;

public sealed class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "CandyGo";
    public string Audience { get; set; } = "CandyGoApps";
    public int ExpiresMinutes { get; set; } = 120;
}
