using TradingScanner.Domain.MarketData;

namespace TradingScanner.Domain.TrendDetection.Configuration;

public static class RollingSlopeConfigurationValidator
{
    public const int RequiredLookbackCandles = 84;
    public const int RequiredAtrPeriod = 14;
    public const decimal RequiredTouchToleranceAtr = 0.3m;
    public const int RequiredMinimumDurationDays = 3;
    public const int RequiredMinimumConfirmedRetests = 1;

    public static void Validate(RollingSlopeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (!options.Enabled)
        {
            errors.Add("Rolling slope trend detector must be enabled ('enabled' = true).");
        }

        if (string.IsNullOrWhiteSpace(options.Timeframe))
        {
            errors.Add("Timeframe is required.");
        }
        else
        {
            try
            {
                _ = Timeframe.Parse(options.Timeframe);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                errors.Add($"Invalid Timeframe '{options.Timeframe}': {ex.Message}");
            }
        }

        if (!options.ClosedCandlesOnly)
        {
            errors.Add("candle_requirements.closed_candles_only must be true.");
        }

        if (!string.Equals(options.CandleOrder, "oldest_first", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"candle_requirements.order must be 'oldest_first' (got '{options.CandleOrder}').");
        }

        if (options.LookbackCandles != RequiredLookbackCandles)
        {
            errors.Add(
                $"candle_requirements.lookback_candles must be {RequiredLookbackCandles} (got {options.LookbackCandles}).");
        }

        if (!options.EvaluateNewCandleAgainstPreviousWindow)
        {
            errors.Add("candle_requirements.evaluate_new_candle_against_previous_window must be true.");
        }

        if (!string.Equals(options.RegressionMethod, "linear_regression", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"regression.method must be 'linear_regression' (got '{options.RegressionMethod}').");
        }

        if (!string.Equals(options.PriceTransform, "none", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"regression.price_transform must be 'none' (got '{options.PriceTransform}').");
        }

        if (!string.Equals(options.UpperBoundarySource, "high", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"regression.upper_boundary_source must be 'high' (got '{options.UpperBoundarySource}').");
        }

        if (!string.Equals(options.LowerBoundarySource, "low", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"regression.lower_boundary_source must be 'low' (got '{options.LowerBoundarySource}').");
        }

        if (options.MinimumValidWindow != RequiredLookbackCandles)
        {
            errors.Add(
                $"regression.minimum_valid_window must be {RequiredLookbackCandles} (got {options.MinimumValidWindow}).");
        }

        if (options.AtrPeriod != RequiredAtrPeriod)
        {
            errors.Add($"atr.period must be {RequiredAtrPeriod} (got {options.AtrPeriod}).");
        }

        if (!string.Equals(options.AtrMethod, "wilder", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"atr.method must be 'wilder' (got '{options.AtrMethod}').");
        }

        if (!string.Equals(options.AtrReference, "previous_closed_candle", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"atr.reference must be 'previous_closed_candle' (got '{options.AtrReference}').");
        }

        if (!options.MinimumSlopeAtrPerCandle.HasValue || options.MinimumSlopeAtrPerCandle.Value <= 0m)
        {
            errors.Add(
                "direction_rules.minimum_slope_atr_per_candle (MinimumSlopeAtrPerCandle) is required and must be > 0. " +
                "Supply a calibrated value via configuration or environment variable override.");
        }

        if (options.TouchToleranceAtr != RequiredTouchToleranceAtr)
        {
            errors.Add(
                $"boundary_interactions.touch_tolerance_atr must be {RequiredTouchToleranceAtr} (got {options.TouchToleranceAtr}).");
        }

        if (!options.BoundaryInteractionClosedCandleRequired)
        {
            errors.Add("boundary_interactions.closed_candle_required must be true.");
        }

        if (!options.UpperBreakoutThresholdAtr.HasValue || options.UpperBreakoutThresholdAtr.Value <= 0m)
        {
            errors.Add(
                "UpperBreakoutThresholdAtr (minimum_distance_atr) is required and must be > 0. " +
                "Supply a calibrated value via configuration or environment variable override.");
        }

        if (!options.MaturityEnabled)
        {
            errors.Add("trend_maturity.enabled must be true.");
        }

        if (options.MinimumDurationDays != RequiredMinimumDurationDays)
        {
            errors.Add(
                $"trend_maturity.minimum_duration_days must be {RequiredMinimumDurationDays} (got {options.MinimumDurationDays}).");
        }

        if (options.MinimumConfirmedBoundaryRetests != RequiredMinimumConfirmedRetests)
        {
            errors.Add(
                $"trend_maturity.minimum_confirmed_boundary_retests must be {RequiredMinimumConfirmedRetests} (got {options.MinimumConfirmedBoundaryRetests}).");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Rolling slope trend detector configuration is invalid:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(e => $" - {e}")));
        }
    }
}
