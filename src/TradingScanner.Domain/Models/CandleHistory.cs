using TradingScanner.Domain.Entities;

namespace TradingScanner.Domain.Models;

public sealed record CandleHistory(
    string Symbol,
    string Timeframe,
    DateTimeOffset LoadedAt,
    DateTimeOffset OldestCandleTime,
    DateTimeOffset LatestCandleTime,
    IReadOnlyList<Candle> Candles
);