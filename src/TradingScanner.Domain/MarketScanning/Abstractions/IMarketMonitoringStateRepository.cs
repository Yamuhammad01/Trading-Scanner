using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Domain.MarketScanning.Abstractions;

public interface IMarketMonitoringStateRepository
{
    Task<MarketMonitoringState?> GetAsync(
        string provider,
        string canonicalSymbol,
        string timeframe,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketMonitoringState>> GetAllAsync(
        string provider,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        MarketMonitoringState state,
        CancellationToken cancellationToken = default);
}
