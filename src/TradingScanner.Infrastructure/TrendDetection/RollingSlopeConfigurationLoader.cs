using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TradingScanner.Domain.TrendDetection.Configuration;

namespace TradingScanner.Infrastructure.TrendDetection;

public static class RollingSlopeConfigurationLoader
{
    public static RollingSlopeOptions LoadAndValidate(
        string jsonFilePath,
        IConfiguration? overridesConfiguration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);

        if (!File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException(
                $"Rolling slope configuration file not found at '{jsonFilePath}'.",
                jsonFilePath);
        }

        var json = File.ReadAllText(jsonFilePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var candleReq = root.GetProperty("candle_requirements");
        var regression = root.GetProperty("regression");
        var atr = root.GetProperty("atr");
        var directionRules = root.GetProperty("direction_rules");
        var boundary = root.GetProperty("boundary_interactions");
        var maturity = root.GetProperty("trend_maturity");

        var bearishBreakThreshold = GetNullableDecimal(
            boundary.GetProperty("bearish").GetProperty("upper_boundary_break"),
            "minimum_distance_atr");
        var bullishBreakThreshold = GetNullableDecimal(
            boundary.GetProperty("bullish").GetProperty("lower_boundary_break"),
            "minimum_distance_atr");

        if (bearishBreakThreshold.HasValue &&
            bullishBreakThreshold.HasValue &&
            bearishBreakThreshold.Value != bullishBreakThreshold.Value)
        {
            throw new InvalidOperationException(
                "Conflicting breakout thresholds in JSON: bearish.upper_boundary_break.minimum_distance_atr " +
                $"({bearishBreakThreshold.Value}) != bullish.lower_boundary_break.minimum_distance_atr ({bullishBreakThreshold.Value}).");
        }

        var options = new RollingSlopeOptions
        {
            SchemaVersion = root.GetProperty("schema_version").GetString() ?? string.Empty,
            Name = root.GetProperty("name").GetString() ?? string.Empty,
            Description = root.GetProperty("description").GetString() ?? string.Empty,
            Enabled = root.GetProperty("enabled").GetBoolean(),
            Timeframe = root.GetProperty("timeframe").GetString() ?? string.Empty,

            ClosedCandlesOnly = candleReq.GetProperty("closed_candles_only").GetBoolean(),
            CandleOrder = candleReq.GetProperty("order").GetString() ?? string.Empty,
            LookbackCandles = candleReq.GetProperty("lookback_candles").GetInt32(),
            EvaluateNewCandleAgainstPreviousWindow =
                candleReq.GetProperty("evaluate_new_candle_against_previous_window").GetBoolean(),

            RegressionMethod = regression.GetProperty("method").GetString() ?? string.Empty,
            PriceTransform = regression.GetProperty("price_transform").GetString() ?? string.Empty,
            UpperBoundarySource = regression.GetProperty("upper_boundary_source").GetString() ?? string.Empty,
            LowerBoundarySource = regression.GetProperty("lower_boundary_source").GetString() ?? string.Empty,
            MinimumValidWindow = regression.GetProperty("minimum_valid_window").GetInt32(),

            AtrPeriod = atr.GetProperty("period").GetInt32(),
            AtrMethod = atr.GetProperty("method").GetString() ?? string.Empty,
            AtrReference = atr.GetProperty("reference").GetString() ?? string.Empty,

            MinimumSlopeAtrPerCandle = GetNullableDecimal(directionRules, "minimum_slope_atr_per_candle"),

            TouchToleranceAtr = boundary.GetProperty("touch_tolerance_atr").GetDecimal(),
            BoundaryInteractionClosedCandleRequired =
                boundary.GetProperty("closed_candle_required").GetBoolean(),

            UpperBreakoutThresholdAtr = bearishBreakThreshold ?? bullishBreakThreshold,

            MaturityEnabled = maturity.GetProperty("enabled").GetBoolean(),
            MinimumDurationDays = maturity.GetProperty("minimum_duration_days").GetInt32(),
            MinimumConfirmedBoundaryRetests =
                maturity.GetProperty("minimum_confirmed_boundary_retests").GetInt32()
        };

        if (overridesConfiguration is not null)
        {
            var section = overridesConfiguration.GetSection("RollingSlope");

            if (decimal.TryParse(section["MinimumSlopeAtrPerCandle"], out var minSlope))
            {
                options.MinimumSlopeAtrPerCandle = minSlope;
            }

            if (decimal.TryParse(section["UpperBreakoutThresholdAtr"], out var breakoutThreshold))
            {
                options.UpperBreakoutThresholdAtr = breakoutThreshold;
            }

            var timeframeOverride = section["Timeframe"];
            if (!string.IsNullOrWhiteSpace(timeframeOverride))
            {
                options.Timeframe = timeframeOverride;
            }
        }

        RollingSlopeConfigurationValidator.Validate(options);
        return options;
    }

    private static decimal? GetNullableDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return prop.GetDecimal();
    }
}
