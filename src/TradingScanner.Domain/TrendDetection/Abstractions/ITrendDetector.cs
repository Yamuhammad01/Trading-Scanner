using TradingScanner.Domain.Entities;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;

namespace TradingScanner.Domain.TrendDetection.Abstractions;

public interface ITrendDetector
{
    Task<TrendResult> EvaluateAsync(
        string provider,
        string symbol,
        string timeframe,
        IReadOnlyList<Candle> closedCandles,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrendResult>> ReplayAsync(
        string provider,
        string symbol,
        string timeframe,
        IReadOnlyList<Candle> closedCandles,
        CancellationToken cancellationToken = default);
}
