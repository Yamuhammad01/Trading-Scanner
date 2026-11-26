namespace TradingScanner.Domain.TrendDetection.RollingSlope.Models;

public sealed record TrendChannel(
    TrendBoundary Upper,
    TrendBoundary Lower,
    decimal Atr,
    TrendDirection Direction,
    bool IsValid,
    string? InvalidReason = null);
