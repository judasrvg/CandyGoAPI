namespace CandyGo.Api.DTOs;

public sealed class BusinessRuleDto
{
    public decimal DeliveryFee { get; set; }
    public decimal RewardPercent { get; set; }
    public decimal CashConversionRate { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class PublicSettingsDto
{
    public decimal DeliveryFee { get; set; }
}

public sealed class UpdateBusinessRuleRequest
{
    public decimal DeliveryFee { get; set; }
    public decimal RewardPercent { get; set; }
    public decimal CashConversionRate { get; set; }
}
