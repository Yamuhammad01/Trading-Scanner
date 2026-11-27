using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Infrastructure.MarketScanning.Discovery;

public sealed class MarketRankingService : IMarketRankingService
{
    private readonly MarketUniverseOptions _options;

    public MarketRankingService(MarketUniverseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<MarketUniverseEntry> RankAndSelect(
        IReadOnlyList<(MarketInstrument Instrument, MarketTicker Ticker)> eligibleMarkets,
        DateTimeOffset discoveredAt)
    {
        ArgumentNullException.ThrowIfNull(eligibleMarkets);

        var maxMarkets = Math.Max(0, _options.MaxActiveMarkets);
        if (maxMarkets == 0 || eligibleMarkets.Count == 0)
        {
            return [];
        }

        IEnumerable<(MarketInstrument Instrument, MarketTicker Ticker)> ordered = _options.SelectionMode switch
        {
            MarketSelectionMode.TopByQuoteVolume => eligibleMarkets
                .OrderByDescending(x => x.Ticker.QuoteVolume24h)
                .ThenBy(x => x.Instrument.CanonicalSymbol, StringComparer.OrdinalIgnoreCase),
            _ => throw new NotSupportedException($"Unsupported selection mode: {_options.SelectionMode}")
        };

        return ordered
            .Take(maxMarkets)
            .Select((pair, index) => new MarketUniverseEntry
            {
                Provider = pair.Instrument.Provider,
                CanonicalSymbol = pair.Instrument.CanonicalSymbol,
                NativeSymbol = pair.Instrument.NativeSymbol,
                SettlementCurrency = pair.Instrument.SettlementCurrency,
                ContractStatus = pair.Instrument.Status,
                IsEligible = true,
                IsActive = true,
                Rank = index + 1,
                QuoteVolume24h = pair.Ticker.QuoteVolume24h,
                LastPrice = pair.Ticker.LastPrice,
                QuantoMultiplier = pair.Instrument.QuantoMultiplier,
                DiscoveredAt = discoveredAt,
                LastTickerUpdate = pair.Ticker.Timestamp
            })
            .ToList();
    }
}
