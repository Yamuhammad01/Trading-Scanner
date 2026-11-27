using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Domain.MarketScanning.Abstractions;

public interface IMarketRankingService
{
    IReadOnlyList<MarketUniverseEntry> RankAndSelect(
        IReadOnlyList<(MarketInstrument Instrument, MarketTicker Ticker)> eligibleMarkets,
        DateTimeOffset discoveredAt);
}
