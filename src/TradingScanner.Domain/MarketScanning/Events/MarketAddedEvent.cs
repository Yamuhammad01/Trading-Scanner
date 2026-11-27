namespace TradingScanner.Domain.MarketScanning.Events;

public sealed record MarketAddedEvent(
    string Provider,
    string CanonicalSymbol,
    string NativeSymbol,
    int Rank,
    decimal QuoteVolume24h,
    DateTimeOffset Timestamp);
