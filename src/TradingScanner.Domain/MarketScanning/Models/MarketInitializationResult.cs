namespace TradingScanner.Domain.MarketScanning.Models;

public sealed record MarketInitializationResult(
    string Provider,
    string CanonicalSymbol,
    string NativeSymbol,
    bool Succeeded,
    int LoadedCandles,
    DateTimeOffset? LatestClosedCandleTime,
    string? ErrorMessage = null);
