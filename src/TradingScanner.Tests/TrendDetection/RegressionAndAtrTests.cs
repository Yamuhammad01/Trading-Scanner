using TradingScanner.Domain.Entities;
using TradingScanner.Domain.TrendDetection.RollingSlope;
using Xunit;

namespace TradingScanner.Tests.TrendDetection;

public sealed class RegressionAndAtrTests
{
    [Fact]
    public void RegressionCalculator_LinearSeries_ComputesExactSlopeInterceptAndProjection()
    {
        var calculator = new RegressionCalculator();
        // y = 100 + 2 * x for x = 0..83
        var values = Enumerable.Range(0, 84).Select(x => 100m + (2m * x)).ToArray();

        var result = calculator.Calculate(values, projectionIndex: 84);

        Assert.Equal(2m, result.Slope);
        Assert.Equal(100m, result.Intercept);
        Assert.Equal(268m, result.ProjectedValue);
    }

    [Fact]
    public void RegressionCalculator_FewerThanTwoPoints_ThrowsArgumentException()
    {
        var calculator = new RegressionCalculator();
        Assert.Throws<ArgumentException>(() => calculator.Calculate([100m], 1));
    }

    [Fact]
    public void AtrCalculator_KnownConstantRangeSeries_ReturnsExactWilderAtr()
    {
        var calculator = new AtrCalculator();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // 20 candles each with High - Low = 10, Close = 100 (TR = 10 for every candle)
        var candles = Enumerable.Range(0, 20)
            .Select(i => new Candle(
                "BTCUSDT",
                start.AddHours(4 * i),
                100m, 105m, 95m, 100m, 10m, 1000m))
            .ToList();

        var atr = calculator.CalculateWilderAtr(candles, endIndexExclusive: 19, period: 14);

        Assert.Equal(10m, atr);
    }

    [Fact]
    public void AtrCalculator_ExcludesEvaluationCandle()
    {
        var calculator = new AtrCalculator();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 16)
            .Select(i => new Candle(
                "BTCUSDT",
                start.AddHours(4 * i),
                100m, 105m, 95m, 100m, 10m, 1000m))
            .ToList();

        // Make the evaluation candle (index 15) have a massive range
        candles[15] = new Candle("BTCUSDT", start.AddHours(60), 100m, 5000m, 1m, 100m, 10m, 1000m);

        var atr = calculator.CalculateWilderAtr(candles, endIndexExclusive: 15, period: 14);

        Assert.Equal(10m, atr);
    }

    [Fact]
    public void AtrCalculator_InsufficientWarmupCandles_ReturnsNull()
    {
        var calculator = new AtrCalculator();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 14)
            .Select(i => new Candle("BTCUSDT", start.AddHours(4 * i), 100m, 105m, 95m, 100m, 10m, 1000m))
            .ToList();

        var atr = calculator.CalculateWilderAtr(candles, endIndexExclusive: 14, period: 14);

        Assert.Null(atr);
    }

    [Fact]
    public void AtrCalculator_ZeroRangeFlatCandles_ReturnsNull()
    {
        var calculator = new AtrCalculator();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 20)
            .Select(i => new Candle("BTCUSDT", start.AddHours(4 * i), 100m, 100m, 100m, 100m, 0m, 0m))
            .ToList();

        var atr = calculator.CalculateWilderAtr(candles, endIndexExclusive: 19, period: 14);

        Assert.Null(atr);
    }
}
