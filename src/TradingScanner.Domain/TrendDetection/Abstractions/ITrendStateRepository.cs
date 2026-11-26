using TradingScanner.Domain.TrendDetection.RollingSlope.Models;

namespace TradingScanner.Domain.TrendDetection.Abstractions;

public interface ITrendStateRepository
{
    Task<TrendState?> GetAsync(
        string provider,
        string symbol,
        string timeframe,
        string configurationId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        TrendState state,
        CancellationToken cancellationToken = default);
}
