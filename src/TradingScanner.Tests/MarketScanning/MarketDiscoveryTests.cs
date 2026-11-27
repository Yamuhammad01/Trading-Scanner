using System.Net;
using System.Text;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Models;
using TradingScanner.Infrastructure.GateIo;
using TradingScanner.Infrastructure.MarketScanning.Discovery;
using TradingScanner.Infrastructure.MarketScanning.Persistence;

namespace TradingScanner.Tests.MarketScanning;

public sealed class MarketDiscoveryTests
{
    [Fact]
    public async Task GateIoDiscovery_MapsContractsAndTickersAndNormalizesSymbols()
    {
        var contractsJson = """
        [
          { "name": "BTC_USDT", "type": "direct", "quanto_multiplier": "0.0001", "in_delisting": false, "status": "trading", "expire_time": 0 },
          { "name": "ETH_USDT", "type": "direct", "quanto_multiplier": "0.01", "in_delisting": false, "status": "trading" },
          { "name": "OLD_USDT", "type": "direct", "quanto_multiplier": "1", "in_delisting": true, "status": "trading" },
          { "name": "EXP_USDT", "type": "direct", "quanto_multiplier": "1", "in_delisting": false, "status": "trading", "expire_time": 1700000000 }
        ]
        """;

        var tickersJson = """
        [
          { "contract": "BTC_USDT", "last": "95000.5", "mark_price": "95001.0", "volume_24h": "1000000", "volume_24h_base": "100", "volume_24h_quote": "9500000", "volume_24h_settle": "9500000" },
          { "contract": "ETH_USDT", "last": "3200.0", "mark_price": "3201.0", "volume_24h": "500000", "volume_24h_base": "5000", "volume_24h_quote": "16000000", "volume_24h_settle": "16000000" }
        ]
        """;

        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/contracts", StringComparison.Ordinal))
            {
                return JsonResponse(contractsJson);
            }

            if (req.RequestUri!.AbsolutePath.EndsWith("/tickers", StringComparison.Ordinal))
            {
                return JsonResponse(tickersJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var normalizer = new GateIoSymbolNormalizer();
        var cache = new GateIoContractMetadataCache(60);
        var now = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);
        var provider = new GateIoMarketDiscoveryProvider(
            new HttpClient(handler),
            new GateIoMarketDataProviderOptions(),
            cache,
            normalizer,
            new FixedTimeProvider(now));

        var markets = await provider.GetAvailableMarketsAsync();
        var tickers = await provider.GetMarketTickersAsync();

        Assert.Equal(4, markets.Count);
        var btc = markets.Single(m => m.NativeSymbol == "BTC_USDT");
        Assert.Equal("BTCUSDT", btc.CanonicalSymbol);
        Assert.True(btc.IsPerpetual);
        Assert.True(btc.IsActive);
        Assert.Equal(0.0001m, btc.QuantoMultiplier);

        var old = markets.Single(m => m.NativeSymbol == "OLD_USDT");
        Assert.False(old.IsActive);
        Assert.True(old.InDelisting);

        var exp = markets.Single(m => m.NativeSymbol == "EXP_USDT");
        Assert.False(exp.IsPerpetual);
        Assert.False(exp.IsActive);

        Assert.Equal(2, tickers.Count);
        var ethTicker = tickers.Single(t => t.NativeSymbol == "ETH_USDT");
        Assert.Equal("ETHUSDT", ethTicker.CanonicalSymbol);
        Assert.Equal(16_000_000m, ethTicker.QuoteVolume24h);
        Assert.Equal("BTC_USDT", normalizer.ToNativeSymbol("BTCUSDT"));
    }

    [Fact]
    public async Task UniverseManager_FiltersRanksAndPreservesInactiveOnRemoval()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"universe-{Guid.NewGuid():N}.json");
        try
        {
            var options = new MarketUniverseOptions
            {
                MaxActiveMarkets = 2,
                Minimum24HourQuoteVolume = 1_000_000m
            };

            var fakeProvider = new FakeDiscoveryProvider();
            var now = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);
            fakeProvider.SetData(
                [
                    Instrument("BTC_USDT", "BTCUSDT", true, true),
                    Instrument("ETH_USDT", "ETHUSDT", true, true),
                    Instrument("SOL_USDT", "SOLUSDT", true, true),
                    Instrument("LOW_USDT", "LOWUSDT", true, true),
                    Instrument("DEAD_USDT", "DEADUSDT", true, false)
                ],
                [
                    Ticker("BTC_USDT", "BTCUSDT", 50_000_000m),
                    Ticker("ETH_USDT", "ETHUSDT", 30_000_000m),
                    Ticker("SOL_USDT", "SOLUSDT", 20_000_000m),
                    Ticker("LOW_USDT", "LOWUSDT", 500_000m),
                    Ticker("DEAD_USDT", "DEADUSDT", 99_000_000m)
                ]);

            var manager = new MarketUniverseManager(
                fakeProvider,
                new MarketEligibilityFilter(options),
                new MarketRankingService(options),
                new FileMarketUniverseRepository(tempFile),
                new GateIoSymbolNormalizer(),
                new FixedTimeProvider(now));

            var diff1 = await manager.RefreshAsync();
            Assert.Equal(2, diff1.Added.Count);
            Assert.Equal(["BTCUSDT", "ETHUSDT"], manager.CurrentUniverse.ActiveMarkets.Select(m => m.CanonicalSymbol).ToArray());

            // Now SOL overtakes ETH in quote volume
            fakeProvider.SetData(
                [
                    Instrument("BTC_USDT", "BTCUSDT", true, true),
                    Instrument("ETH_USDT", "ETHUSDT", true, true),
                    Instrument("SOL_USDT", "SOLUSDT", true, true)
                ],
                [
                    Ticker("BTC_USDT", "BTCUSDT", 50_000_000m),
                    Ticker("ETH_USDT", "ETHUSDT", 10_000_000m),
                    Ticker("SOL_USDT", "SOLUSDT", 40_000_000m)
                ]);

            var diff2 = await manager.RefreshAsync();
            Assert.Single(diff2.Added);
            Assert.Equal("SOLUSDT", diff2.Added[0].CanonicalSymbol);
            Assert.Single(diff2.Removed);
            Assert.Equal("ETHUSDT", diff2.Removed[0].CanonicalSymbol);
            Assert.Equal(["BTCUSDT", "SOLUSDT"], manager.CurrentUniverse.ActiveMarkets.Select(m => m.CanonicalSymbol).ToArray());

            // ETHUSDT is preserved as inactive in universe history
            var ethEntry = manager.CurrentUniverse.Markets.Single(m => m.CanonicalSymbol == "ETHUSDT");
            Assert.False(ethEntry.IsActive);
            Assert.NotNull(ethEntry.DeactivatedAt);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static MarketInstrument Instrument(string native, string canonical, bool perp, bool active) => new()
    {
        Provider = "Gate.io USDT Perpetual Futures",
        NativeSymbol = native,
        CanonicalSymbol = canonical,
        SettlementCurrency = "USDT",
        IsPerpetual = perp,
        IsActive = active,
        Status = active ? "trading" : "delisted",
        QuantoMultiplier = 0.01m,
        DiscoveredAt = DateTimeOffset.UtcNow
    };

    private static MarketTicker Ticker(string native, string canonical, decimal quoteVol) => new()
    {
        Provider = "Gate.io USDT Perpetual Futures",
        NativeSymbol = native,
        CanonicalSymbol = canonical,
        LastPrice = 100m,
        MarkPrice = 100m,
        QuoteVolume24h = quoteVol,
        BaseVolume24h = quoteVol / 100m,
        ContractVolume24h = quoteVol / 100m,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class FakeDiscoveryProvider : IMarketDiscoveryProvider
    {
        private IReadOnlyList<MarketInstrument> _instruments = [];
        private IReadOnlyList<MarketTicker> _tickers = [];

        public string ProviderName => "Gate.io USDT Perpetual Futures";

        public void SetData(IReadOnlyList<MarketInstrument> instruments, IReadOnlyList<MarketTicker> tickers)
        {
            _instruments = instruments;
            _tickers = tickers;
        }

        public Task<IReadOnlyList<MarketInstrument>> GetAvailableMarketsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_instruments);

        public Task<IReadOnlyList<MarketTicker>> GetMarketTickersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_tickers);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
