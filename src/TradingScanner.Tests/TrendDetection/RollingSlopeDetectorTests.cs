using TradingScanner.Domain.Entities;
using TradingScanner.Domain.TrendDetection.Abstractions;
using TradingScanner.Domain.TrendDetection.Configuration;
using TradingScanner.Domain.TrendDetection.RollingSlope;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;
using Xunit;

namespace TradingScanner.Tests.TrendDetection;

public sealed class RollingSlopeDetectorTests
{
    private static RollingSlopeOptions CreateValidOptions(
        decimal minSlope = 0.05m,
        decimal breakoutThreshold = 1.0m) => new()
        {
            SchemaVersion = "1.0.0",
            Name = "rolling_slope_trend_detector",
            Enabled = true,
            Timeframe = "4H",
            ClosedCandlesOnly = true,
            CandleOrder = "oldest_first",
            LookbackCandles = 84,
            EvaluateNewCandleAgainstPreviousWindow = true,
            RegressionMethod = "linear_regression",
            PriceTransform = "none",
            UpperBoundarySource = "high",
            LowerBoundarySource = "low",
            MinimumValidWindow = 84,
            AtrPeriod = 14,
            AtrMethod = "wilder",
            AtrReference = "previous_closed_candle",
            MinimumSlopeAtrPerCandle = minSlope,
            TouchToleranceAtr = 0.3m,
            BoundaryInteractionClosedCandleRequired = true,
            UpperBreakoutThresholdAtr = breakoutThreshold,
            MaturityEnabled = true,
            MinimumDurationDays = 3,
            MinimumConfirmedBoundaryRetests = 1
        };

    private static RollingSlopeTrendDetector CreateDetector(
        RollingSlopeOptions options,
        ITrendStateRepository? repo = null) =>
        new(
            options,
            new AtrCalculator(),
            new ChannelBoundaryCalculator(new RegressionCalculator()),
            new TrendStateManager(new BoundaryInteractionEvaluator(), new TrendMaturityEvaluator()),
            repo ?? new InMemoryTrendStateRepository());

    private static List<Candle> BuildLinearTrendCandles(
        int count,
        decimal startMid,
        decimal slopePerCandle,
        decimal halfHeight = 5m,
        DateTimeOffset? startTime = null)
    {
        var t0 = startTime ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var list = new List<Candle>(count);
        for (var i = 0; i < count; i++)
        {
            var mid = startMid + (slopePerCandle * i);
            list.Add(new Candle(
                "BTCUSDT",
                t0.AddHours(4 * i),
                mid,
                mid + halfHeight,
                mid - halfHeight,
                mid,
                100m,
                1000m));
        }
        return list;
    }

    [Fact]
    public async Task EvaluateAsync_InsufficientCandles_ReturnsNeutralNoValidTrend()
    {
        var detector = CreateDetector(CreateValidOptions());
        var candles = BuildLinearTrendCandles(84, 1000m, 2m); // Only 83 prior candles for index 83

        var result = await detector.EvaluateAsync("Bybit", "BTCUSDT", "4H", candles);

        Assert.Equal(TrendDirection.Neutral, result.Direction);
        Assert.Equal(TrendCondition.NoValidTrend, result.Condition);
        Assert.Null(result.UpperBoundaryPrice);
    }

    [Fact]
    public async Task EvaluateAsync_BothSlopesPositive_ReturnsBullish_AndExcludesEvalCandleFromRegression()
    {
        var detector = CreateDetector(CreateValidOptions(minSlope: 0.05m));
        // 85 candles: indices 0..83 are regression window, index 84 is evaluation candle.
        // Slope = 2, ATR ~= 10 => NormSlope = 0.2 > 0.05
        var candles = BuildLinearTrendCandles(85, 1000m, 2m, halfHeight: 5m);
        // Expected upper boundary at x=84: 1005 + 2*84 = 1173
        // Expected lower boundary at x=84: 995 + 2*84 = 1163
        // Give evaluation candle a huge wick that would distort regression if included:
        candles[84] = candles[84] with { High = 5000m, Close = 1168m };

        var result = await detector.EvaluateAsync("Bybit", "BTCUSDT", "4H", candles);

        Assert.Equal(TrendDirection.Bullish, result.Direction);
        Assert.Equal(1173m, result.UpperBoundaryPrice);
        Assert.Equal(1163m, result.LowerBoundaryPrice);
    }

    [Fact]
    public async Task EvaluateAsync_BothSlopesNegative_ReturnsBearish()
    {
        var detector = CreateDetector(CreateValidOptions(minSlope: 0.05m));
        var candles = BuildLinearTrendCandles(85, 2000m, -2m, halfHeight: 5m);
        // Keep evaluation candle inside the channel (away from 0.3*ATR=3 touch zone of lower boundary)
        var mid84 = 2000m - (2m * 84);
        candles[84] = candles[84] with { High = mid84 + 1m, Low = mid84 - 1m, Close = mid84 };

        var result = await detector.EvaluateAsync("Bybit", "BTCUSDT", "4H", candles);

        Assert.Equal(TrendDirection.Bearish, result.Direction);
        Assert.Equal(TrendCondition.Developing, result.Condition);
    }

    [Fact]
    public async Task EvaluateAsync_OpposingSlopes_ReturnsNeutral()
    {
        var detector = CreateDetector(CreateValidOptions(minSlope: 0.05m));
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var candles = new List<Candle>();
        for (var i = 0; i < 85; i++)
        {
            var high = 1050m + (2m * i); // rising highs
            var low = 950m - (2m * i);   // falling lows
            candles.Add(new Candle("BTCUSDT", t0.AddHours(4 * i), 1000m, high, low, 1000m, 10m, 100m));
        }

        var result = await detector.EvaluateAsync("Bybit", "BTCUSDT", "4H", candles);

        Assert.Equal(TrendDirection.Neutral, result.Direction);
        Assert.Equal(TrendCondition.NoValidTrend, result.Condition);
    }

    [Fact]
    public async Task EvaluateAsync_SlopeBelowMinimum_ReturnsNeutral()
    {
        // Slope = 0.1, ATR = 10 => NormSlope = 0.01 < 0.05
        var detector = CreateDetector(CreateValidOptions(minSlope: 0.05m));
        var candles = BuildLinearTrendCandles(85, 1000m, 0.1m, halfHeight: 5m);

        var result = await detector.EvaluateAsync("Bybit", "BTCUSDT", "4H", candles);

        Assert.Equal(TrendDirection.Neutral, result.Direction);
    }

    [Fact]
    public void ChannelBoundaryCalculator_CrossedBoundaries_ReturnsInvalidNeutral()
    {
        var calc = new ChannelBoundaryCalculator(new RegressionCalculator());
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // Construct highs that fall steeply and lows that rise so projected upper <= projected lower at x=84
        var candles = new List<Candle>();
        for (var i = 0; i < 85; i++)
        {
            var high = 1000m - (5m * i);
            var low = 600m + (5m * i);
            candles.Add(new Candle("BTCUSDT", t0.AddHours(4 * i), 800m, Math.Max(high, low) + 1m, Math.Min(high, low), 800m, 10m, 100m));
        }

        // Force exact crossed regression by having High < Low in regression projection?
        // Actually High >= Low in valid candles, but if Highs converge below Lows at projection:
        // At i=0..41 high=1000, low=500; at i=42..83 high=500, low=499 -> upper slope is steeper negative than lower slope!
        candles.Clear();
        for (var i = 0; i < 85; i++)
        {
            var high = 1000m - (10m * i);
            var low = 400m - (1m * i);
            // For i=0..66 high > low; at x=84: upper=160, lower=316 -> crossed!
            var h = Math.Max(high, low + 1m);
            candles.Add(new Candle("BTCUSDT", t0.AddHours(4 * i), low + 1m, h, low, low + 1m, 10m, 100m));
        }

        var channel = calc.Calculate(candles, 84, 84, atr: 10m, minimumSlopeAtrPerCandle: 0.05m);

        Assert.False(channel.IsValid);
        Assert.Equal(TrendDirection.Neutral, channel.Direction);
    }

    [Fact]
    public async Task ReplayAsync_BearishRetestDeduplicationAndMaturity_WorksAsSpecified()
    {
        var detector = CreateDetector(CreateValidOptions(minSlope: 0.05m, breakoutThreshold: 1.5m));
        // 110 candles: trend establishes at index 84 (day 14). Need 3 days (18 x 4h candles) -> index 102+
        var candles = BuildLinearTrendCandles(110, 3000m, -2m, halfHeight: 5m);

        // Keep non-retest candles (84, 85, 88..102, 104..109) inside the channel away from the 0.3*ATR boundary zone
        for (var i = 84; i < candles.Count; i++)
        {
            var mid = 3000m - (2m * i);
            candles[i] = candles[i] with { High = mid + 1m, Low = mid - 1m, Close = mid };
        }

        // At index 86 and 87 (consecutive), touch lower boundary within 0.3*ATR (~3) and close above lower boundary.
        // At i=86: mid = 3000 - 172 = 2828, lower = 2823. Touch low=2824, close=2827 (>2823).
        candles[86] = candles[86] with { Low = 2824m, Close = 2827m };
        candles[87] = candles[87] with { Low = 2822m, Close = 2826m }; // consecutive touch -> should NOT increment retest count

        // At i=88..102, price stays away from lower boundary (mid +- 1).
        // At i=103 (after > 3 days from i=84), touch lower boundary again -> independent retest #2
        var mid103 = 3000m - (2m * 103); // 2794, lower = 2789
        candles[103] = candles[103] with { Low = mid103 - 4m, Close = mid103 };

        var results = await detector.ReplayAsync("Bybit", "BTCUSDT", "4H", candles);

        Assert.Equal(TrendDirection.Bearish, results[84].Direction);
        Assert.Equal(TrendCondition.Developing, results[84].Condition);
        Assert.Equal(candles[84].StartTime, results[84].EstablishedAt);

        // First retest counted at 86, NOT duplicated at 87; < 3 days => Established
        Assert.Equal(1, results[86].BoundaryRetestCount);
        Assert.Equal(TrendCondition.Established, results[86].Condition);
        Assert.Equal(1, results[87].BoundaryRetestCount);

        // EstablishedAt preserved throughout
        Assert.Equal(candles[84].StartTime, results[102].EstablishedAt);
        // At 102 (18 candles = 3 days after 84) with 1 retest => Mature!
        Assert.Equal(TrendCondition.Mature, results[102].Condition);
        // At 103, independent retest #2 counted after leaving interaction area
        Assert.Equal(2, results[103].BoundaryRetestCount);
        Assert.Equal(TrendCondition.Mature, results[103].Condition);
    }

    [Fact]
    public async Task ReplayAsync_AccelerationAndWickRules_WorkForBearishAndBullish()
    {
        var detector = CreateDetector(CreateValidOptions(minSlope: 0.05m));
        var bearCandles = BuildLinearTrendCandles(87, 3000m, -2m, halfHeight: 5m);
        // i=84: establish bearish. i=85: wick below lower boundary, close above -> NOT accelerating.
        var lower85 = 3000m - (2m * 85) - 5m;
        bearCandles[85] = bearCandles[85] with { Low = lower85 - 10m, Close = lower85 + 2m };
        // i=86: close below lower boundary -> Accelerating!
        var lower86 = 3000m - (2m * 86) - 5m;
        bearCandles[86] = bearCandles[86] with { Low = lower86 - 10m, Close = lower86 - 3m };

        var bearResults = await detector.ReplayAsync("Bybit", "BTCUSDT", "4H", bearCandles);
        Assert.NotEqual(TrendCondition.Accelerating, bearResults[85].Condition);
        Assert.Equal(TrendDirection.Bearish, bearResults[86].Direction);
        Assert.Equal(TrendCondition.Accelerating, bearResults[86].Condition);
        Assert.True(bearResults[86].LowerBoundaryBroken);

        // Mirror for Bullish:
        var bullDetector = CreateDetector(CreateValidOptions(minSlope: 0.05m));
        var bullCandles = BuildLinearTrendCandles(87, 1000m, 2m, halfHeight: 5m);
        var upper85 = 1000m + (2m * 85) + 5m;
        bullCandles[85] = bullCandles[85] with { High = upper85 + 10m, Close = upper85 - 2m };
        var upper86 = 1000m + (2m * 86) + 5m;
        bullCandles[86] = bullCandles[86] with { High = upper86 + 10m, Close = upper86 + 3m };

        var bullResults = await bullDetector.ReplayAsync("Bybit", "BTCUSDT", "4H", bullCandles);
        Assert.NotEqual(TrendCondition.Accelerating, bullResults[85].Condition);
        Assert.Equal(TrendDirection.Bullish, bullResults[86].Direction);
        Assert.Equal(TrendCondition.Accelerating, bullResults[86].Condition);
        Assert.True(bullResults[86].UpperBoundaryBroken);
    }

    [Fact]
    public async Task ReplayAsync_SignificantOppositeBreakout_InvalidatesToNeutralPotentialReversal()
    {
        var detector = CreateDetector(CreateValidOptions(minSlope: 0.05m, breakoutThreshold: 1.0m));
        var bearCandles = BuildLinearTrendCandles(88, 3000m, -2m, halfHeight: 5m);
        // i=84: establish bearish.
        // i=85: wick above upper boundary, close inside -> NOT invalidated.
        var upper85 = 3000m - (2m * 85) + 5m;
        bearCandles[85] = bearCandles[85] with { High = upper85 + 20m, Close = upper85 - 1m };
        // i=86: small close above upper boundary (< 1.0 ATR = 10) -> NOT invalidated.
        var upper86 = 3000m - (2m * 86) + 5m;
        bearCandles[86] = bearCandles[86] with { High = upper86 + 5m, Close = upper86 + 4m };
        // i=87: close >= 1.0 ATR above upper boundary -> PotentialReversal!
        var upper87 = 3000m - (2m * 87) + 5m;
        bearCandles[87] = bearCandles[87] with { High = upper87 + 20m, Close = upper87 + 15m };

        var results = await detector.ReplayAsync("Bybit", "BTCUSDT", "4H", bearCandles);

        Assert.Equal(TrendDirection.Bearish, results[85].Direction);
        Assert.Equal(TrendDirection.Bearish, results[86].Direction);
        Assert.Equal(TrendDirection.Neutral, results[87].Direction);
        Assert.Equal(TrendCondition.PotentialReversal, results[87].Condition);
        Assert.True(results[87].SignificantOppositeBoundaryBreakout);
        Assert.Null(results[87].EstablishedAt);
    }

    private sealed class InMemoryTrendStateRepository : ITrendStateRepository
    {
        private readonly Dictionary<string, TrendState> _store = new(StringComparer.OrdinalIgnoreCase);

        public Task<TrendState?> GetAsync(string provider, string symbol, string timeframe, string configurationId, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue($"{provider}|{symbol}|{timeframe}|{configurationId}", out var state);
            return Task.FromResult(state);
        }

        public Task SaveAsync(TrendState state, CancellationToken cancellationToken = default)
        {
            _store[$"{state.Provider}|{state.Symbol}|{state.Timeframe}|{state.ConfigurationId}"] = state;
            return Task.CompletedTask;
        }
    }
}
