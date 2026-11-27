using Microsoft.Extensions.Configuration;
using TradingScanner.Domain.Entities;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Events;
using TradingScanner.Domain.MarketScanning.Models;
using TradingScanner.Domain.TrendDetection.RollingSlope;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;
using TradingScanner.Infrastructure.MarketScanning.Monitoring;
using TradingScanner.Infrastructure.MarketScanning.Persistence;
using TradingScanner.Infrastructure.MarketScanning.Processing;
using TradingScanner.Infrastructure.TrendDetection;

namespace TradingScanner.Tests.MarketScanning;

public sealed class TrendDetectionAndPersistenceTests
{
    [Fact]
    public async Task TrendDetector_EvaluatesRollingSlope_IsolatesStateBetweenMarkets()
    {
        var tempMonitoringFile = Path.GetTempFileName();
        var tempTrendFile = Path.GetTempFileName();
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "Configuration", "rolling-slope-4h.json");
            var overrides = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RollingSlope:MinimumSlopeAtrPerCandle"] = "0.02",
                    ["RollingSlope:UpperBreakoutThresholdAtr"] = "1.0"
                })
                .Build();

            var options = RollingSlopeConfigurationLoader.LoadAndValidate(configPath, overrides);

            var atrCalc = new AtrCalculator();
            var regressionCalc = new RegressionCalculator();
            var channelCalc = new ChannelBoundaryCalculator(regressionCalc);
            var maturityEvaluator = new TrendMaturityEvaluator();
            var interactionEvaluator = new BoundaryInteractionEvaluator();
            var stateManager = new TrendStateManager(interactionEvaluator, maturityEvaluator);
            var trendStateRepo = new FileTrendStateRepository(tempTrendFile);
            var monitoringRepo = new FileMarketMonitoringStateRepository(tempMonitoringFile);

            var trendDetector = new RollingSlopeTrendDetector(
                options,
                atrCalc,
                channelCalc,
                stateManager,
                trendStateRepo);

            var readiness = TrendDetectorReadiness.Ready(trendDetector);
            var buffer = new MarketCandleBuffer(250);

            var processor = new ClosedCandleProcessor(
                buffer,
                readiness,
                monitoringRepo,
                trendStateRepo);

            // Generate 90 historical candles for BTC (uptrend) and ETH (downtrend)
            var baseTime = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
            var btcCandles = new List<Candle>();
            var ethCandles = new List<Candle>();

            for (int i = 0; i < 90; i++)
            {
                var time = baseTime.AddHours(i * 4);
                // BTC ascending
                var btcPrice = 90000m + (i * 100m);
                btcCandles.Add(new Candle("BTCUSDT", time, btcPrice - 10, btcPrice + 50, btcPrice - 50, btcPrice, 100, 1000));

                // ETH descending
                var ethPrice = 3500m - (i * 10m);
                ethCandles.Add(new Candle("ETHUSDT", time, ethPrice + 5, ethPrice + 20, ethPrice - 20, ethPrice, 50, 500));
            }

            buffer.Initialize("GateIo", "BTCUSDT", "4H", btcCandles.Take(89));
            buffer.Initialize("GateIo", "ETHUSDT", "4H", ethCandles.Take(89));

            // Process 90th candle for BTC
            var btcLast = btcCandles[89];
            await processor.ProcessAsync(new ClosedCandleEvent("GateIo", "BTCUSDT", "BTC_USDT", "4H", btcLast.StartTime, btcLast));

            // Process 90th candle for ETH
            var ethLast = ethCandles[89];
            await processor.ProcessAsync(new ClosedCandleEvent("GateIo", "ETHUSDT", "ETH_USDT", "4H", ethLast.StartTime, ethLast));

            // Verify BTC state in trend repository
            var btcTrendState = await trendStateRepo.GetAsync("GateIo", "BTCUSDT", "4H", options.ConfigurationId);
            var ethTrendState = await trendStateRepo.GetAsync("GateIo", "ETHUSDT", "4H", options.ConfigurationId);

            Assert.NotNull(btcTrendState);
            Assert.NotNull(ethTrendState);

            // Isolation: BTC trend is Bullish, ETH trend is Bearish
            Assert.Equal(TrendDirection.Bullish, btcTrendState!.Direction);
            Assert.Equal(TrendDirection.Bearish, ethTrendState!.Direction);

            // Established time is preserved and independent
            Assert.NotNull(btcTrendState.EstablishedAt);
            Assert.NotNull(ethTrendState.EstablishedAt);

            // Monitoring state persistence
            var btcMonState = await monitoringRepo.GetAsync("GateIo", "BTCUSDT", "4H");
            var ethMonState = await monitoringRepo.GetAsync("GateIo", "ETHUSDT", "4H");

            Assert.Equal(btcLast.StartTime, btcMonState!.LastProcessedCandleTime);
            Assert.Equal(ethLast.StartTime, ethMonState!.LastProcessedCandleTime);
            Assert.Equal(TrendDirection.Bullish, btcMonState.TrendDirection);
            Assert.Equal(TrendDirection.Bearish, ethMonState.TrendDirection);
        }
        finally
        {
            File.Delete(tempMonitoringFile);
            File.Delete(tempTrendFile);
        }
    }

    [Fact]
    public async Task Persistence_SurvivesRestart_StateRestoredAccurately()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var established = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
            var lastProcessed = new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);

            // Step 1: Save state with instance 1
            var repo1 = new FileMarketMonitoringStateRepository(tempFile);
            await repo1.SaveAsync(new MarketMonitoringState
            {
                Provider = "GateIo",
                CanonicalSymbol = "SOLUSDT",
                NativeSymbol = "SOL_USDT",
                Timeframe = "4H",
                IsInitialized = true,
                IsActive = true,
                TrendDirection = TrendDirection.Bullish,
                TrendCondition = TrendCondition.Mature,
                TrendEstablishedAt = established,
                ConfirmedRetestCount = 3,
                LastProcessedCandleTime = lastProcessed
            });

            // Step 2: "Restart" - load with fresh instance 2
            var repo2 = new FileMarketMonitoringStateRepository(tempFile);
            var restored = await repo2.GetAsync("GateIo", "SOLUSDT", "4H");

            Assert.NotNull(restored);
            Assert.True(restored!.IsInitialized);
            Assert.True(restored.IsActive);
            Assert.Equal(TrendDirection.Bullish, restored.TrendDirection);
            Assert.Equal(TrendCondition.Mature, restored.TrendCondition);
            Assert.Equal(established, restored.TrendEstablishedAt);
            Assert.Equal(3, restored.ConfirmedRetestCount);
            Assert.Equal(lastProcessed, restored.LastProcessedCandleTime);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
