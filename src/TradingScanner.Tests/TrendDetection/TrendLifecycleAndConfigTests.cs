using Microsoft.Extensions.Configuration;
using TradingScanner.Domain.Entities;
using TradingScanner.Domain.TrendDetection.Configuration;
using TradingScanner.Domain.TrendDetection.RollingSlope;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;
using TradingScanner.Infrastructure.TrendDetection;
using Xunit;

namespace TradingScanner.Tests.TrendDetection;

public sealed class TrendLifecycleAndConfigTests
{
    private static RollingSlopeOptions CreateValidOptions() => new()
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
        MinimumSlopeAtrPerCandle = 0.05m,
        TouchToleranceAtr = 0.3m,
        BoundaryInteractionClosedCandleRequired = true,
        UpperBreakoutThresholdAtr = 1.0m,
        MaturityEnabled = true,
        MinimumDurationDays = 3,
        MinimumConfirmedBoundaryRetests = 1
    };

    [Fact]
    public async Task FileTrendStateRepository_Restart_RestoresEstablishedAtAndRetestCount_AndPreventsDuplicates()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"trend-state-{Guid.NewGuid():N}.json");
        try
        {
            var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var candles = Enumerable.Range(0, 87)
                .Select(i =>
                {
                    var mid = 3000m - (2m * i);
                    return new Candle("BTCUSDT", t0.AddHours(4 * i), mid, mid + 5m, mid - 5m, mid, 10m, 100m);
                })
                .ToList();

            // Retest at index 85
            var mid85 = 3000m - (2m * 85);
            candles[85] = candles[85] with { Low = mid85 - 4m, Close = mid85 };

            var repo1 = new FileTrendStateRepository(tempFile);
            var detector1 = new RollingSlopeTrendDetector(
                CreateValidOptions(),
                new AtrCalculator(),
                new ChannelBoundaryCalculator(new RegressionCalculator()),
                new TrendStateManager(new BoundaryInteractionEvaluator(), new TrendMaturityEvaluator()),
                repo1);

            await detector1.ReplayAsync("Bybit", "BTCUSDT", "4H", candles.Take(86).ToList());

            // Simulate restart with a brand new repository & detector instance pointing to the same file
            var repo2 = new FileTrendStateRepository(tempFile);
            var detector2 = new RollingSlopeTrendDetector(
                CreateValidOptions(),
                new AtrCalculator(),
                new ChannelBoundaryCalculator(new RegressionCalculator()),
                new TrendStateManager(new BoundaryInteractionEvaluator(), new TrendMaturityEvaluator()),
                repo2);

            // Re-evaluating candle 85 should be skipped as duplicate and preserve state
            var duplicateResult = await detector2.EvaluateAsync("Bybit", "BTCUSDT", "4H", candles.Take(86).ToList());
            Assert.Equal("Candle already processed.", duplicateResult.DiagnosticReason);
            Assert.Equal(candles[84].StartTime, duplicateResult.EstablishedAt);
            Assert.Equal(1, duplicateResult.BoundaryRetestCount);

            // Evaluating candle 86 continues lifecycle with preserved EstablishedAt and retest count
            var nextResult = await detector2.EvaluateAsync("Bybit", "BTCUSDT", "4H", candles);
            Assert.Equal(TrendDirection.Bearish, nextResult.Direction);
            Assert.Equal(candles[84].StartTime, nextResult.EstablishedAt);
            Assert.Equal(1, nextResult.BoundaryRetestCount);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task FileTrendStateRepository_IsolatesByProviderSymbolAndTimeframe()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"trend-iso-{Guid.NewGuid():N}.json");
        try
        {
            var repo = new FileTrendStateRepository(tempFile);
            var s1 = TrendState.CreateInitial("Bybit", "BTCUSDT", "4H", "cfg") with { Direction = TrendDirection.Bullish };
            var s2 = TrendState.CreateInitial("GateIo", "BTCUSDT", "4H", "cfg") with { Direction = TrendDirection.Bearish };
            var s3 = TrendState.CreateInitial("Bybit", "ETHUSDT", "4H", "cfg") with { Direction = TrendDirection.Neutral };
            var s4 = TrendState.CreateInitial("Bybit", "BTCUSDT", "1H", "cfg") with { Direction = TrendDirection.Bearish };

            await repo.SaveAsync(s1);
            await repo.SaveAsync(s2);
            await repo.SaveAsync(s3);
            await repo.SaveAsync(s4);

            Assert.Equal(TrendDirection.Bullish, (await repo.GetAsync("Bybit", "BTCUSDT", "4H", "cfg"))!.Direction);
            Assert.Equal(TrendDirection.Bearish, (await repo.GetAsync("GateIo", "BTCUSDT", "4H", "cfg"))!.Direction);
            Assert.Equal(TrendDirection.Neutral, (await repo.GetAsync("Bybit", "ETHUSDT", "4H", "cfg"))!.Direction);
            Assert.Equal(TrendDirection.Bearish, (await repo.GetAsync("Bybit", "BTCUSDT", "1H", "cfg"))!.Direction);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigurationValidator_MissingThresholds_ThrowsInvalidOperationException()
    {
        var options = CreateValidOptions();
        options.MinimumSlopeAtrPerCandle = null;
        options.UpperBreakoutThresholdAtr = null;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RollingSlopeConfigurationValidator.Validate(options));

        Assert.Contains("MinimumSlopeAtrPerCandle", ex.Message);
        Assert.Contains("UpperBreakoutThresholdAtr", ex.Message);
    }

    [Fact]
    public void ConfigurationLoader_LoadsJsonAndAppliesOverrides()
    {
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "Configuration", "rolling-slope-4h.json");
        if (!File.Exists(jsonPath))
        {
            jsonPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Configuration", "rolling-slope-4h.json"));
        }

        // Without overrides, uncalibrated nulls must fail validation
        Assert.Throws<InvalidOperationException>(() =>
            RollingSlopeConfigurationLoader.LoadAndValidate(jsonPath));

        // With overrides, loads and validates cleanly
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RollingSlope:MinimumSlopeAtrPerCandle"] = "0.02",
                ["RollingSlope:UpperBreakoutThresholdAtr"] = "1.0"
            })
            .Build();

        var options = RollingSlopeConfigurationLoader.LoadAndValidate(jsonPath, config);

        Assert.Equal(0.02m, options.MinimumSlopeAtrPerCandle);
        Assert.Equal(1.0m, options.UpperBreakoutThresholdAtr);
        Assert.Equal(84, options.LookbackCandles);
        Assert.Equal(14, options.AtrPeriod);
        Assert.Equal(0.3m, options.TouchToleranceAtr);
    }
}
