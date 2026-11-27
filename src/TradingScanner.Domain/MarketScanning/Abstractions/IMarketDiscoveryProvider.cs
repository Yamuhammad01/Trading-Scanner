using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Domain.MarketScanning.Abstractions;

public interface IMarketDiscoveryProvider
{
    string ProviderName { get; }

    Task<IReadOnlyList<MarketInstrument>> GetAvailableMarketsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketTicker>> GetMarketTickersAsync(
        CancellationToken cancellationToken = default);
}
