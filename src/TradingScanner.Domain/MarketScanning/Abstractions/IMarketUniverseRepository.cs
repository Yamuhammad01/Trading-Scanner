using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Domain.MarketScanning.Abstractions;

public interface IMarketUniverseRepository
{
    Task<MarketUniverse?> GetAsync(
        string provider,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        MarketUniverse universe,
        CancellationToken cancellationToken = default);
}
