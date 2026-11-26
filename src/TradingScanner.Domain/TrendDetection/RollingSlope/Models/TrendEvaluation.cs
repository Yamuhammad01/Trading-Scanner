namespace TradingScanner.Domain.TrendDetection.RollingSlope.Models;

public sealed record TrendEvaluation(
    TrendResult Result,
    TrendState UpdatedState,
    bool WasSkippedAsDuplicate = false,
    bool IsInsufficientData = false);
