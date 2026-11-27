namespace TradingScanner.Domain.MarketScanning.Events;

public sealed record ClosedCandleEvent(
    string Provider,
    string CanonicalSymbol,
    string NativeSymbol,
    string Timeframe,
    DateTimeOffset CandleStartTime,
    Domain.Entities.Candle Candle,
    bool IsFromRecovery = false);
