using TradingScanner.Domain.Entities;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Models;
using TradingScanner.Domain.TrendDetection.Abstractions;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;
using TradingScanner.Infrastructure.MarketScanning.Initialization;
using TradingScanner.Infrastructure.MarketScanning.Monitoring;
using TradingScanner.Infrastructure.MarketScanning.Persistence;
using TradingScanner.Infrastructure.Services;

namespace TradingScanner.Tests.MarketScanning;

public sealed class MarketInitializationTests
{
    [Fact]
    public async Task InitializeSingleMarket_LoadsHistoricalCandlesAndMarksReady()
    {
        var now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var tempMonitoringFile = Path.GetTempFileName();
        var tempTrendFile = Path.GetTempFileName();

        try
        {
            var provider = new FakeMarketDataProvider();
            var buffer = new MarketCandleBuffer(250);
            var monitoringRepo = new FileMarketMonitoringStateRepository(tempMonitoringFile);
            var trendRepo = new FakeTrendStateRepository();
            var readiness = TrendDetectorReadiness.NotReady("Not configured for test");

            var options = new MarketInitializationOptions
            {
                Timeframe = "4H",
                HistoricalLookbackCandles = 120,
                MaxConcurrentInitializations = 5
            };

            var service = new MarketInitializationService(
                provider,
                new HistoricalCandleLoader(provider, new CandleValidator(), timeProvider),
                buffer,
                monitoringRepo,
                trendRepo,
                readiness,
                options,
                rollingSlopeOptions: null,
                timeProvider);

            var market = new MarketUniverseEntry
            {
                Provider = "Gate.io USDT Perpetual Futures",
                CanonicalSymbol = "BTCUSDT",
                NativeSymbol = "BTC_USDT",
                SettlementCurrency = "USDT",
                IsEligible = true,
                IsActive = true,
                Rank = 1,
                QuoteVolume24h = 50_000_000m,
                LastPrice = 95000m,
                QuantoMultiplier = 0.0001m,
                DiscoveredAt = now,
                LastTickerUpdate = now
            };

            var result = await service.InitializeSingleMarketAsync(market);

            Assert.True(result.Succeeded);
            Assert.True(result.LoadedCandles > 0);
            Assert.NotNull(result.LatestClosedCandleTime);

            // Verify buffer has candles.
            var snapshot = buffer.GetSnapshot("Gate.io USDT Perpetual Futures", "BTCUSDT", "4H");
            Assert.NotEmpty(snapshot);
            Assert.Equal(result.LoadedCandles, snapshot.Count);

            // Verify monitoring state was persisted.
            var state = await monitoringRepo.GetAsync(
                "Gate.io USDT Perpetual Futures", "BTCUSDT", "4H");
            Assert.NotNull(state);
            Assert.True(state!.IsInitialized);
            Assert.True(state.IsActive);
            Assert.Null(state.LastError);
        }
        finally
        {
            File.Delete(tempMonitoringFile);
        }
    }

    [Fact]
    public async Task InitializeMarkets_ConcurrencyBounded_AllSucceed()
    {
        var now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var tempFile = Path.GetTempFileName();

        try
        {
            var provider = new FakeMarketDataProvider();
            var buffer = new MarketCandleBuffer(250);
            var monitoringRepo = new FileMarketMonitoringStateRepository(tempFile);
            var trendRepo = new FakeTrendStateRepository();
            var readiness = TrendDetectorReadiness.NotReady("Not configured for test");

            var options = new MarketInitializationOptions
            {
                Timeframe = "4H",
                HistoricalLookbackCandles = 120,
                MaxConcurrentInitializations = 2 // Low to test bounding
            };

            var service = new MarketInitializationService(
                provider,
                new HistoricalCandleLoader(provider, new CandleValidator(), timeProvider),
                buffer,
                monitoringRepo,
                trendRepo,
                readiness,
                options,
                rollingSlopeOptions: null,
                timeProvider);

            var markets = Enumerable.Range(1, 5).Select(i => new MarketUniverseEntry
            {
                Provider = "Gate.io USDT Perpetual Futures",
                CanonicalSymbol = $"TOKEN{i}USDT",
                NativeSymbol = $"TOKEN{i}_USDT",
                SettlementCurrency = "USDT",
                IsEligible = true,
                IsActive = true,
                Rank = i,
                QuoteVolume24h = 1_000_000m,
                LastPrice = 100m,
                QuantoMultiplier = 1m,
                DiscoveredAt = now,
                LastTickerUpdate = now
            }).ToList();

            var results = await service.InitializeMarketsAsync(markets);

            Assert.Equal(5, results.Count);
            Assert.All(results, r => Assert.True(r.Succeeded, $"Market {r.CanonicalSymbol} failed: {r.ErrorMessage}"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task InitializeMarkets_OneFailsOthersContinue()
    {
        var now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var tempFile = Path.GetTempFileName();

        try
        {
            var provider = new FailingOnSymbolMarketDataProvider("FAILUSDT");
            var buffer = new MarketCandleBuffer(250);
            var monitoringRepo = new FileMarketMonitoringStateRepository(tempFile);
            var trendRepo = new FakeTrendStateRepository();
            var readiness = TrendDetectorReadiness.NotReady("Not configured for test");

            var options = new MarketInitializationOptions
            {
                Timeframe = "4H",
                HistoricalLookbackCandles = 120,
                MaxConcurrentInitializations = 5
            };

            var service = new MarketInitializationService(
                provider,
                new HistoricalCandleLoader(provider, new CandleValidator(), timeProvider),
                buffer,
                monitoringRepo,
                trendRepo,
                readiness,
                options,
                rollingSlopeOptions: null,
                timeProvider);

            var markets = new List<MarketUniverseEntry>
            {
                MakeEntry("BTCUSDT", "BTC_USDT", 1),
                MakeEntry("FAILUSDT", "FAIL_USDT", 2),
                MakeEntry("ETHUSDT", "ETH_USDT", 3)
            };

            var results = await service.InitializeMarketsAsync(markets);

            Assert.Equal(3, results.Count);
            var btc = results.Single(r => r.CanonicalSymbol == "BTCUSDT");
            var fail = results.Single(r => r.CanonicalSymbol == "FAILUSDT");
            var eth = results.Single(r => r.CanonicalSymbol == "ETHUSDT");

            Assert.True(btc.Succeeded);
            Assert.False(fail.Succeeded);
            Assert.NotNull(fail.ErrorMessage);
            Assert.True(eth.Succeeded);

            // Failed market should have its error persisted.
            var failState = await monitoringRepo.GetAsync(
                "Gate.io USDT Perpetual Futures", "FAILUSDT", "4H");
            Assert.NotNull(failState);
            Assert.False(failState!.IsInitialized);
            Assert.NotNull(failState.LastError);
            Assert.True(failState.ConsecutiveErrors >= 1);
        }
        finally
        {
            File.Delete(tempFile);
        }

        MarketUniverseEntry MakeEntry(string canonical, string native, int rank) => new()
        {
            Provider = "Gate.io USDT Perpetual Futures",
            CanonicalSymbol = canonical,
            NativeSymbol = native,
            SettlementCurrency = "USDT",
            IsEligible = true,
            IsActive = true,
            Rank = rank,
            QuoteVolume24h = 1_000_000m,
            LastPrice = 100m,
            QuantoMultiplier = 1m,
            DiscoveredAt = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero),
            LastTickerUpdate = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero)
        };
    }

    [Fact]
    public void CandleBuffer_InitializeAndAppend_MaintainsSortedOrder()
    {
        var buffer = new MarketCandleBuffer(10);
        var candles = Enumerable.Range(0, 5).Select(i => new Candle(
            "BTCUSDT",
            new DateTimeOffset(2026, 11, 26, i * 4, 0, 0, TimeSpan.Zero),
            95000m + i, 95100m + i, 94900m + i, 95050m + i, 100m, 1000m))
            .ToList();

        buffer.Initialize("GateIo", "BTCUSDT", "4H", candles);

        var snapshot = buffer.GetSnapshot("GateIo", "BTCUSDT", "4H");
        Assert.Equal(5, snapshot.Count);
        Assert.Equal(candles[0].StartTime, snapshot[0].StartTime);
        Assert.Equal(candles[4].StartTime, snapshot[4].StartTime);

        // Append a new candle.
        var newCandle = new Candle("BTCUSDT",
            new DateTimeOffset(2026, 11, 26, 20, 0, 0, TimeSpan.Zero),
            95100m, 95200m, 95000m, 95150m, 100m, 1000m);
        var appended = buffer.TryAppend("GateIo", "BTCUSDT", "4H", newCandle);
        Assert.True(appended);

        snapshot = buffer.GetSnapshot("GateIo", "BTCUSDT", "4H");
        Assert.Equal(6, snapshot.Count);
        Assert.Equal(newCandle.StartTime, snapshot[^1].StartTime);

        // Duplicate append returns false.
        var duplicate = buffer.TryAppend("GateIo", "BTCUSDT", "4H", newCandle);
        Assert.False(duplicate);
    }

    [Fact]
    public void CandleBuffer_TrimsOldestWhenExceedingMax()
    {
        var buffer = new MarketCandleBuffer(3);
        var candles = Enumerable.Range(0, 5).Select(i => new Candle(
            "ETHUSDT",
            new DateTimeOffset(2026, 11, 26, i * 4, 0, 0, TimeSpan.Zero),
            3200m + i, 3300m + i, 3100m + i, 3250m + i, 50m, 500m))
            .ToList();

        buffer.Initialize("GateIo", "ETHUSDT", "4H", candles);

        var snapshot = buffer.GetSnapshot("GateIo", "ETHUSDT", "4H");
        Assert.Equal(3, snapshot.Count);
        // Should have kept the 3 newest candles.
        Assert.Equal(candles[2].StartTime, snapshot[0].StartTime);
        Assert.Equal(candles[4].StartTime, snapshot[^1].StartTime);
    }

    // ── Helpers ──

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeTrendStateRepository : ITrendStateRepository
    {
        private readonly Dictionary<string, TrendState> _states = new(StringComparer.OrdinalIgnoreCase);

        public Task<TrendState?> GetAsync(string provider, string symbol, string timeframe, string configurationId,
            CancellationToken cancellationToken = default)
        {
            var key = $"{provider}|{symbol}|{timeframe}|{configurationId}";
            _states.TryGetValue(key, out var state);
            return Task.FromResult(state);
        }

        public Task SaveAsync(TrendState state, CancellationToken cancellationToken = default)
        {
            var key = $"{state.Provider}|{state.Symbol}|{state.Timeframe}|{state.ConfigurationId}";
            _states[key] = state;
            return Task.CompletedTask;
        }
    }

    private class FakeMarketDataProvider : IMarketDataProvider
    {
        public string ProviderName => "Gate.io USDT Perpetual Futures";

        public virtual Task<IReadOnlyList<Candle>> GetCandlesAsync(
            MarketDataRequest request, CancellationToken cancellationToken = default)
        {
            var interval = request.Timeframe.Interval;
            var start = request.StartTime ?? DateTimeOffset.UtcNow.AddDays(-14);
            var end = request.EndTime ?? DateTimeOffset.UtcNow;

            var candles = new List<Candle>();
            for (var t = start; t <= end; t = t.Add(interval))
            {
                candles.Add(new Candle(
                    request.Symbol,
                    t,
                    Open: 100m + (decimal)(t.ToUnixTimeSeconds() % 100),
                    High: 110m + (decimal)(t.ToUnixTimeSeconds() % 100),
                    Low: 90m + (decimal)(t.ToUnixTimeSeconds() % 100),
                    Close: 105m + (decimal)(t.ToUnixTimeSeconds() % 100),
                    Volume: 1000m,
                    Turnover: 100000m));
            }

            return Task.FromResult<IReadOnlyList<Candle>>(candles);
        }
    }

    private sealed class FailingOnSymbolMarketDataProvider(string failSymbol) : FakeMarketDataProvider
    {
        public override Task<IReadOnlyList<Candle>> GetCandlesAsync(
            MarketDataRequest request, CancellationToken cancellationToken = default)
        {
            if (string.Equals(request.Symbol, failSymbol, StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpRequestException($"Simulated API failure for {failSymbol}");
            }

            return base.GetCandlesAsync(request, cancellationToken);
        }
    }
}
