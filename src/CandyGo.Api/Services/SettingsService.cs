using CandyGo.Api.DTOs;
using CandyGo.Api.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;

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

        try
        {
            var rules = await connection.QuerySingleOrDefaultAsync<BusinessRuleDto>(@"
SELECT TOP (1)
    delivery_fee,
    reward_percent,
    cash_conversion_rate,
    CONVERT(VARCHAR(5), store_open_time, 108) AS store_open_time,
    CONVERT(VARCHAR(5), store_close_time, 108) AS store_close_time,
    updated_at,
    updated_by
FROM cg.business_rules
WHERE id = 1");

            if (rules is null)
            {
                throw new InvalidOperationException("No existen reglas de negocio configuradas.");
            }

            rules.StoreOpenTime = NormalizeTimeOrDefault(rules.StoreOpenTime, "09:00");
            rules.StoreCloseTime = NormalizeTimeOrDefault(rules.StoreCloseTime, "18:00");
            return rules;
        }
        catch (SqlException ex) when (ex.Number == 207)
        {
            // Fallback temporal para DB sin migración de horarios.
            var legacy = await connection.QuerySingleOrDefaultAsync<LegacyBusinessRuleRow>(@"
SELECT TOP (1)
    delivery_fee,
    reward_percent,
    cash_conversion_rate,
    updated_at,
    updated_by
FROM cg.business_rules
WHERE id = 1");

            if (legacy is null)
            {
                throw new InvalidOperationException("No existen reglas de negocio configuradas.");
            }

            return new BusinessRuleDto
            {
                DeliveryFee = legacy.DeliveryFee,
                RewardPercent = legacy.RewardPercent,
                CashConversionRate = legacy.CashConversionRate,
                StoreOpenTime = "09:00",
                StoreCloseTime = "18:00",
                UpdatedAt = legacy.UpdatedAt,
                UpdatedBy = legacy.UpdatedBy
            };
        }
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

        if (!IsValidHourMinute(request.StoreOpenTime))
        {
            throw new ArgumentException("StoreOpenTime debe tener formato HH:mm (24h).");
        }

        if (!IsValidHourMinute(request.StoreCloseTime))
        {
            throw new ArgumentException("StoreCloseTime debe tener formato HH:mm (24h).");
        }

        await using var connection = _connectionFactory.CreateConnection();

        var hasScheduleColumns = await connection.ExecuteScalarAsync<int>(@"
SELECT CASE
    WHEN COL_LENGTH('cg.business_rules', 'store_open_time') IS NOT NULL
     AND COL_LENGTH('cg.business_rules', 'store_close_time') IS NOT NULL
    THEN 1 ELSE 0 END;");

        if (hasScheduleColumns != 1)
        {
            throw new InvalidOperationException("La BD de CandyGo requiere migración de horario. Ejecuta el script 004_store_schedule.sql.");
        }

        var affected = await connection.ExecuteAsync(@"
UPDATE cg.business_rules
SET delivery_fee = @DeliveryFee,
    reward_percent = @RewardPercent,
    cash_conversion_rate = @CashConversionRate,
    store_open_time = CONVERT(TIME(0), @StoreOpenTime),
    store_close_time = CONVERT(TIME(0), @StoreCloseTime),
    updated_by = @UpdatedBy,
    updated_at = SYSUTCDATETIME()
WHERE id = 1",
            new
            {
                request.DeliveryFee,
                request.RewardPercent,
                request.CashConversionRate,
                StoreOpenTime = request.StoreOpenTime,
                StoreCloseTime = request.StoreCloseTime,
                UpdatedBy = updatedBy
            });

        if (affected == 0)
        {
            throw new InvalidOperationException("No se pudo actualizar reglas de negocio.");
        }

        return await GetBusinessRulesAsync();
    }

    private static bool IsValidHourMinute(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return TimeSpan.TryParseExact(text, @"hh\:mm", null, out _);
    }

    private static string NormalizeTimeOrDefault(string? value, string fallback)
    {
        var text = (value ?? string.Empty).Trim();
        return IsValidHourMinute(text) ? text : fallback;
    }

    private sealed class LegacyBusinessRuleRow
    {
        public decimal DeliveryFee { get; set; }
        public decimal RewardPercent { get; set; }
        public decimal CashConversionRate { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }
}
