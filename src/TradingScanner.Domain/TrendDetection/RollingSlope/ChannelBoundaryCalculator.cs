using TradingScanner.Domain.Entities;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;

namespace TradingScanner.Domain.TrendDetection.RollingSlope;

public sealed class ChannelBoundaryCalculator
{
    private readonly RegressionCalculator _regressionCalculator;

    public ChannelBoundaryCalculator(RegressionCalculator regressionCalculator)
    {
        _regressionCalculator = regressionCalculator;
    }

    public TrendChannel Calculate(
        IReadOnlyList<Candle> candles,
        int evaluationIndex,
        int lookbackCandles,
        decimal atr,
        decimal minimumSlopeAtrPerCandle)
    {
        ArgumentNullException.ThrowIfNull(candles);

        if (evaluationIndex < lookbackCandles)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evaluationIndex),
                $"At least {lookbackCandles} prior candles are required.");
        }

        if (atr <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(atr), "ATR must be positive.");
        }

        var startIndex = evaluationIndex - lookbackCandles;
        Span<decimal> highs = lookbackCandles <= 256
            ? stackalloc decimal[lookbackCandles]
            : new decimal[lookbackCandles];
        Span<decimal> lows = lookbackCandles <= 256
            ? stackalloc decimal[lookbackCandles]
            : new decimal[lookbackCandles];

        for (var i = 0; i < lookbackCandles; i++)
        {
            var candle = candles[startIndex + i];
            highs[i] = candle.High;
            lows[i] = candle.Low;
        }

        var upperReg = _regressionCalculator.Calculate(highs, lookbackCandles);
        var lowerReg = _regressionCalculator.Calculate(lows, lookbackCandles);

        var upperNormSlope = upperReg.Slope / atr;
        var lowerNormSlope = lowerReg.Slope / atr;

        var upperBoundary = new TrendBoundary(
            upperReg.Intercept,
            upperReg.Slope,
            upperReg.ProjectedValue,
            upperNormSlope);

        var lowerBoundary = new TrendBoundary(
            lowerReg.Intercept,
            lowerReg.Slope,
            lowerReg.ProjectedValue,
            lowerNormSlope);

        if (upperReg.ProjectedValue <= lowerReg.ProjectedValue)
        {
            return new TrendChannel(
                upperBoundary,
                lowerBoundary,
                atr,
                TrendDirection.Neutral,
                IsValid: false,
                InvalidReason: "Projected upper boundary is at or below projected lower boundary.");
        }

        if (upperNormSlope > minimumSlopeAtrPerCandle &&
            lowerNormSlope > minimumSlopeAtrPerCandle)
        {
            return new TrendChannel(
                upperBoundary,
                lowerBoundary,
                atr,
                TrendDirection.Bullish,
                IsValid: true);
        }

        if (upperNormSlope < -minimumSlopeAtrPerCandle &&
            lowerNormSlope < -minimumSlopeAtrPerCandle)
        {
            return new TrendChannel(
                upperBoundary,
                lowerBoundary,
                atr,
                TrendDirection.Bearish,
                IsValid: true);
        }

        return new TrendChannel(
            upperBoundary,
            lowerBoundary,
            atr,
            TrendDirection.Neutral,
            IsValid: false,
            InvalidReason: "Normalized slopes do not meet directional magnitude or agreement requirements.");
    }
}
