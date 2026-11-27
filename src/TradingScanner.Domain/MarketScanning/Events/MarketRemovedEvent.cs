namespace TradingScanner.Domain.MarketScanning.Events;

public sealed record MarketRemovedEvent(
    string Provider,
    string CanonicalSymbol,
    string NativeSymbol,
    string Reason,
    DateTimeOffset Timestamp);
