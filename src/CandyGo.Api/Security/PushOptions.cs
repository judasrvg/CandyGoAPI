namespace CandyGo.Api.Security;

public sealed class PushOptions
{
    public string VapidSubject { get; set; } = string.Empty;
    public string VapidPublicKey { get; set; } = string.Empty;
    public string VapidPrivateKey { get; set; } = string.Empty;
    public string DefaultIconUrl { get; set; } = "/assets/candygo-icon.svg";
    public string DefaultTargetUrl { get; set; } = "/";
}
