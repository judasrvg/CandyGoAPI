using CandyGo.Api.DTOs;
using CandyGo.Api.Infrastructure;
using Dapper;

namespace CandyGo.Api.Services;

public interface ISettingsService
{
    Task<BusinessRuleDto> GetBusinessRulesAsync();
    Task<BusinessRuleDto> UpdateBusinessRulesAsync(UpdateBusinessRuleRequest request, string updatedBy);
}

public sealed class SettingsService : ISettingsService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SettingsService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<BusinessRuleDto> GetBusinessRulesAsync()
    {
        await using var connection = _connectionFactory.CreateConnection();

        var rules = await connection.QuerySingleOrDefaultAsync<BusinessRuleDto>(@"
SELECT TOP (1)
    delivery_fee,
    reward_percent,
    cash_conversion_rate,
    updated_at,
    updated_by
FROM cg.business_rules
WHERE id = 1");

        if (rules is null)
        {
            throw new InvalidOperationException("No existen reglas de negocio configuradas.");
        }

        return rules;
    }

    public async Task<BusinessRuleDto> UpdateBusinessRulesAsync(UpdateBusinessRuleRequest request, string updatedBy)
    {
        if (request.DeliveryFee < 0)
        {
            throw new ArgumentException("DeliveryFee no puede ser negativo.");
        }

        if (request.RewardPercent < 0 || request.RewardPercent > 100)
        {
            throw new ArgumentException("RewardPercent debe estar entre 0 y 100.");
        }

        if (request.CashConversionRate <= 0)
        {
            throw new ArgumentException("CashConversionRate debe ser mayor que 0.");
        }

        await using var connection = _connectionFactory.CreateConnection();

        var affected = await connection.ExecuteAsync(@"
UPDATE cg.business_rules
SET delivery_fee = @DeliveryFee,
    reward_percent = @RewardPercent,
    cash_conversion_rate = @CashConversionRate,
    updated_by = @UpdatedBy,
    updated_at = SYSUTCDATETIME()
WHERE id = 1",
            new
            {
                request.DeliveryFee,
                request.RewardPercent,
                request.CashConversionRate,
                UpdatedBy = updatedBy
            });

        if (affected == 0)
        {
            throw new InvalidOperationException("No se pudo actualizar reglas de negocio.");
        }

        return await GetBusinessRulesAsync();
    }
}
