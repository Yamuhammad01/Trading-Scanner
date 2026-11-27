using System.Threading.Channels;
using TradingScanner.Domain.Entities;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Events;
using TradingScanner.Domain.MarketScanning.Models;
using TradingScanner.Domain.TrendDetection.Abstractions;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;
using TradingScanner.Infrastructure.MarketScanning.Monitoring;
using TradingScanner.Infrastructure.MarketScanning.Persistence;
using TradingScanner.Infrastructure.MarketScanning.Processing;
using TradingScanner.Infrastructure.MarketScanning.Recovery;
using TradingScanner.Infrastructure.Services;

namespace TradingScanner.Tests.MarketScanning;

public sealed class ConcurrentMonitoringAndRecoveryTests
{
    [Fact]
    public async Task ClosedCandleProcessor_EvaluatesAndPreventsDuplicates()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var buffer = new MarketCandleBuffer(250);
            var monitoringRepo = new FileMarketMonitoringStateRepository(tempFile);
            var trendRepo = new FakeTrendStateRepository();
            var fakeDetector = new FakeTrendDetector();
            var readiness = TrendDetectorReadiness.Ready(fakeDetector);
            var now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

            var processor = new ClosedCandleProcessor(
                buffer,
                readiness,
                monitoringRepo,
                trendRepo,
                timeProvider: new FixedTimeProvider(now));

            var candleTime = new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);
            var candle = new Candle("BTCUSDT", candleTime, 95000m, 96000m, 94000m, 95500m, 1000m, 100000m);
            var evt = new ClosedCandleEvent(
                Provider: "GateIo",
                CanonicalSymbol: "BTCUSDT",
                NativeSymbol: "BTC_USDT",
                Timeframe: "4H",
                CandleStartTime: candleTime,
                Candle: candle);

            // First processing
            await processor.ProcessAsync(evt);
            Assert.Equal(1, fakeDetector.EvaluationCount);

            // Duplicate processing should be ignored
            await processor.ProcessAsync(evt);
            Assert.Equal(1, fakeDetector.EvaluationCount);

            var state = await monitoringRepo.GetAsync("GateIo", "BTCUSDT", "4H");
            Assert.NotNull(state);
            Assert.Equal(candleTime, state!.LastProcessedCandleTime);
            Assert.Equal(TrendDirection.Bullish, state.TrendDirection);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ProcessingCoordinator_IsolatesMarkets_ProcessesCandlesChronologically()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var buffer = new MarketCandleBuffer(250);
            var monitoringRepo = new FileMarketMonitoringStateRepository(tempFile);
            var trendRepo = new FakeTrendStateRepository();
            var fakeDetector = new FakeTrendDetector();
            var readiness = TrendDetectorReadiness.Ready(fakeDetector);
            var now = new DateTimeOffset(2026, 11, 26, 16, 0, 0, TimeSpan.Zero);

            var processor = new ClosedCandleProcessor(
                buffer,
                readiness,
                monitoringRepo,
                trendRepo,
                timeProvider: new FixedTimeProvider(now));

            var options = new MarketMonitoringOptions();
            await using var coordinator = new MarketProcessingCoordinator(processor, options);
            using var cts = new CancellationTokenSource();
            coordinator.Start(cts.Token);

            var btcTime1 = new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);
            var btcTime2 = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
            var ethTime1 = new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);

            var evtBtc1 = new ClosedCandleEvent("GateIo", "BTCUSDT", "BTC_USDT", "4H", btcTime1,
                new Candle("BTCUSDT", btcTime1, 95000, 96000, 94000, 95500, 100, 1000));
            var evtBtc2 = new ClosedCandleEvent("GateIo", "BTCUSDT", "BTC_USDT", "4H", btcTime2,
                new Candle("BTCUSDT", btcTime2, 95500, 97000, 95000, 96500, 100, 1000));
            var evtEth1 = new ClosedCandleEvent("GateIo", "ETHUSDT", "ETH_USDT", "4H", ethTime1,
                new Candle("ETHUSDT", ethTime1, 3200, 3300, 3100, 3250, 50, 500));

            await coordinator.EnqueueAsync(evtBtc1);
            await coordinator.EnqueueAsync(evtEth1);
            await coordinator.EnqueueAsync(evtBtc2);

            // Give coordinator worker tasks a brief moment to process
            await Task.Delay(200);

            var btcState = await monitoringRepo.GetAsync("GateIo", "BTCUSDT", "4H");
            var ethState = await monitoringRepo.GetAsync("GateIo", "ETHUSDT", "4H");

            Assert.NotNull(btcState);
            Assert.Equal(btcTime2, btcState!.LastProcessedCandleTime);

            Assert.NotNull(ethState);
            Assert.Equal(ethTime1, ethState!.LastProcessedCandleTime);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RestReconciliation_RecoversMissedCandlesChronologically()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var now = new DateTimeOffset(2026, 11, 26, 16, 30, 0, TimeSpan.Zero); // Latest closed 4H is 12:00
            var timeProvider = new FixedTimeProvider(now);
            var fakeProvider = new ReconcileMarketDataProvider();
            var validator = new CandleValidator();
            var buffer = new MarketCandleBuffer(250);
            var monitoringRepo = new FileMarketMonitoringStateRepository(tempFile);

            // Set last processed time to 04:00 (so 08:00 and 12:00 are missing)
            var lastProcessed = new DateTimeOffset(2026, 11, 26, 4, 0, 0, TimeSpan.Zero);
            await monitoringRepo.SaveAsync(new MarketMonitoringState
            {
                Provider = "Gate.io USDT Perpetual Futures",
                CanonicalSymbol = "BTCUSDT",
                NativeSymbol = "BTC_USDT",
                Timeframe = "4H",
                IsInitialized = true,
                IsActive = true,
                LastProcessedCandleTime = lastProcessed
            });

            var initOptions = new MarketInitializationOptions { Timeframe = "4H" };
            var reconciler = new RestReconciliationService(
                fakeProvider,
                validator,
                buffer,
                monitoringRepo,
                initOptions,
                timeProvider);

            var market = new MarketUniverseEntry
            {
                Provider = "Gate.io USDT Perpetual Futures",
                CanonicalSymbol = "BTCUSDT",
                NativeSymbol = "BTC_USDT",
                SettlementCurrency = "USDT",
                IsEligible = true,
                IsActive = true,
                QuoteVolume24h = 1000000m,
                LastPrice = 95000m,
                QuantoMultiplier = 0.0001m,
                DiscoveredAt = now,
                LastTickerUpdate = now
            };

            var recoveredEvents = await reconciler.ReconcileAsync(market);

            Assert.Equal(2, recoveredEvents.Count);
            Assert.Equal(new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero), recoveredEvents[0].CandleStartTime);
            Assert.Equal(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero), recoveredEvents[1].CandleStartTime);
            Assert.All(recoveredEvents, e => Assert.True(e.IsFromRecovery));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private sealed class ReconcileMarketDataProvider : IMarketDataProvider
    {
        public string ProviderName => "Gate.io USDT Perpetual Futures";

        public Task<IReadOnlyList<Candle>> GetCandlesAsync(MarketDataRequest request, CancellationToken cancellationToken = default)
        {
            var candles = new List<Candle>
            {
                new("BTCUSDT", new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero), 95000, 96000, 94000, 95500, 100, 1000),
                new("BTCUSDT", new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero), 95500, 97000, 95000, 96500, 100, 1000),
            };
            return Task.FromResult<IReadOnlyList<Candle>>(candles);
        }
    }

    private sealed class FakeTrendDetector : ITrendDetector
    {
        public int EvaluationCount { get; private set; }

        public Task<TrendResult> EvaluateAsync(string provider, string symbol, string timeframe, IReadOnlyList<Candle> closedCandles, CancellationToken cancellationToken = default)
        {
            EvaluationCount++;
            var last = closedCandles[^1];
            return Task.FromResult(new TrendResult
            {
                Provider = provider,
                Symbol = symbol,
                Timeframe = timeframe,
                Direction = TrendDirection.Bullish,
                Condition = TrendCondition.Established,
                EvaluatedAt = DateTimeOffset.UtcNow,
                LastProcessedCandleTime = last.StartTime
            });
        }

        public Task<IReadOnlyList<TrendResult>> ReplayAsync(string provider, string symbol, string timeframe, IReadOnlyList<Candle> closedCandles, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TrendResult>>([]);
        }
    }

    private sealed class FakeTrendStateRepository : ITrendStateRepository
    {
        public Task<TrendState?> GetAsync(string provider, string symbol, string timeframe, string configurationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TrendState?>(null);

        public Task SaveAsync(TrendState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
