using TradingScanner.Domain.Entities;

namespace TradingScanner.Domain.TrendDetection.RollingSlope;

public sealed class AtrCalculator
{
    public decimal? CalculateWilderAtr(
        IReadOnlyList<Candle> candles,
        int endIndexExclusive,
        int period = 14)
    {
        ArgumentNullException.ThrowIfNull(candles);

        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "ATR period must be positive.");
        }

        if (endIndexExclusive > candles.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(endIndexExclusive));
        }

        // Need at least (period + 1) closed candles prior to evaluation candle
        // so that we have `period` true ranges for the initial SMA.
        if (endIndexExclusive < period + 1)
        {
            return null;
        }

        decimal initialSum = 0m;
        for (var i = 1; i <= period; i++)
        {
            initialSum += ComputeTrueRange(candles[i], candles[i - 1].Close);
        }

        var atr = initialSum / period;

        for (var i = period + 1; i < endIndexExclusive; i++)
        {
            var tr = ComputeTrueRange(candles[i], candles[i - 1].Close);
            atr = ((atr * (period - 1)) + tr) / period;
        }

        return atr > 0m ? atr : null;
    }

    private static decimal ComputeTrueRange(Candle current, decimal previousClose)
    {
        var highLow = current.High - current.Low;
        var highPrevClose = Math.Abs(current.High - previousClose);
        var lowPrevClose = Math.Abs(current.Low - previousClose);

        return Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose));
    }
}
