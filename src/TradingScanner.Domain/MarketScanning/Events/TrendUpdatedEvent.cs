namespace TradingScanner.Domain.MarketScanning.Events;

public sealed record TrendUpdatedEvent(
    string Provider,
    string CanonicalSymbol,
    string Timeframe,
    TradingScanner.Domain.TrendDetection.RollingSlope.Models.TrendDirection Direction,
    TradingScanner.Domain.TrendDetection.RollingSlope.Models.TrendCondition Condition,
    TradingScanner.Domain.TrendDetection.RollingSlope.Models.TrendDirection PreviousDirection,
    TradingScanner.Domain.TrendDetection.RollingSlope.Models.TrendCondition PreviousCondition,
    TradingScanner.Domain.TrendDetection.RollingSlope.Models.TrendResult TrendResult,
    DateTimeOffset Timestamp);
