namespace CandyGo.Api.DTOs;

public sealed class BusinessRuleDto
{
    public decimal DeliveryFee { get; set; }
    public decimal RewardPercent { get; set; }
    public decimal CashConversionRate { get; set; }
    public string StoreOpenTime { get; set; } = "09:00";
    public string StoreCloseTime { get; set; } = "18:00";
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class PublicSettingsDto
{
    public decimal DeliveryFee { get; set; }
    public decimal RewardPercent { get; set; }
    public decimal CashConversionRate { get; set; }
    public string StoreOpenTime { get; set; } = "09:00";
    public string StoreCloseTime { get; set; } = "18:00";
}

public sealed class UpdateBusinessRuleRequest
{
    public decimal DeliveryFee { get; set; }
    public decimal RewardPercent { get; set; }
    public decimal CashConversionRate { get; set; }
    public string StoreOpenTime { get; set; } = "09:00";
    public string StoreCloseTime { get; set; } = "18:00";
}
